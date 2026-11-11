# Secure Proxy Web API

A configuration-driven, fail-closed proxy. It forwards only explicitly configured operations to predefined destinations. Built with .NET 8, ASP.NET Core, EF Core and SQL Server.

Design decisions and trade-offs: [DESIGN-NOTES.md](DESIGN-NOTES.md). AI usage: [AI-USAGE.md](AI-USAGE.md).

## Prerequisites

- .NET 8 SDK
- SQL Server (LocalDB, Express or a full instance)
- The EF Core CLI: `dotnet tool install --global dotnet-ef --version 8.*`
- Docker Desktop, running, only for the four integration tests (see [Tests](#tests))

## Setup

All commands run from the repository root.

**1. Connection string**, stored in User Secrets so it never enters the repository. Replace `localhost` with your SQL Server instance name if needed:

```bash
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=localhost;Database=SecureProxy;Trusted_Connection=True;TrustServerCertificate=True" --project src/SecureProxy.Api
```

**2. Demo encryption key**: 32 random bytes, Base64 encoded (44 characters). In PowerShell 7:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Then:

```bash
dotnet user-secrets set "ENCRYPTION_KEY_DEMO" "<the value from above>" --project src/SecureProxy.Api
```

An environment variable with the same name works too. Keys are never stored in `appsettings.json` or in the database; the database stores only the name of the secret.

**3. Database:**

```bash
dotnet ef database update -p src/SecureProxy.Infrastructure -s src/SecureProxy.Api
```

## Configuration

Non-secret settings in `src/SecureProxy.Api/appsettings.json`:

```json
"Proxy": {
  "AllowedHeaders": [ "Idempotency-Key", "X-Request-Id" ],
  "MaxResponse": 1048576
}
```

| Key | Meaning |
| --- | --- |
| `AllowedHeaders` | The only incoming headers forwarded to destinations. `Authorization`, `Cookie`, `Host` and hop-by-hop headers are never forwarded, even if listed. |
| `MaxResponse` | Maximum upstream response body, in bytes. Larger responses return 502. |

Operations, destinations, parameter rules and encryption profiles live in SQL Server.

## Run

```bash
dotnet run --project src/SecureProxy.Api
```

In Development the app seeds demo configuration on first start:

| Operation | Method and path | Notes |
| --- | --- | --- |
| `customer.get` | `GET /users/{urlId}` | `urlId`: required integer, up to 10 digits |
| `invoice.create` | `POST /posts` | Requires an encrypted payload (`ENCRYPTION_KEY_DEMO`) |
| `customer.disabled` | `GET /users/{urlId}` | Disabled, for testing rejection |

The destination is [JSONPlaceholder](https://jsonplaceholder.typicode.com), a public fake API, so requests return real responses.

Swagger UI: `https://localhost:<port>/swagger`.

## Try it

**A valid request** is forwarded to the destination:

```bash
curl -i -X POST https://localhost:<port>/api/proxy/execute \
  -H "Content-Type: application/json" \
  -d '{ "clientId": "customer.get", "json": { "urlId": "1" } }'
```

Response: `200`, user 1 from JSONPlaceholder, and the header `X-Proxy-Source: upstream`.

**Rejected requests** never reach the destination:

| Body | Response |
| --- | --- |
| `{ "clientId": "customer.disabled", "json": { "urlId": "1" } }` | 404 `operation_not_found` |
| `{ "clientId": "unknown.op", "json": {} }` | 404 `operation_not_found` |
| `{ "clientId": "customer.get", "json": { "urlId": "abc" } }` | 400 `invalid_parameters` |
| `{ "clientId": "customer.get", "json": { "urlId": "1", "admin": "true" } }` | 400 `invalid_parameters` |

Proxy errors have the form `{ "error": "<code>", "correlationId": "<id>" }` and carry the header `X-Proxy-Source: proxy`. Every request, forwarded or rejected, writes one row to the `RequestLogs` table with the internal reason.

**Updating configuration with optimistic concurrency.** Read the operation's current `rowVersion` as Base64, for example in SQL Server:

```sql
SELECT o.Id, o.[Key],
       CAST(N'' AS XML).value('xs:base64Binary(sql:column("v.rv"))', 'varchar(20)') AS RowVersion
FROM Operations o
CROSS APPLY (SELECT CAST(o.RowVersion AS varbinary(8)) AS rv) v;
```

Then:

```bash
curl -i -X PUT https://localhost:<port>/api/admin/operations/<id> \
  -H "Content-Type: application/json" \
  -d '{ "isActive": false, "maxRequestSizeBytes": 65536, "rowVersion": "<base64>" }'
```

| Case | Response |
| --- | --- |
| Current version | 200 `{ "rowVersion": "<new base64>" }` |
| Stale version (someone else updated first) | 409 `concurrency_conflict` |
| Unknown id | 404 `operation_not_found` |
| `rowVersion` is not Base64 | 400 `invalid_request` |

This endpoint has no authentication in this exercise; in production it must require an admin role.

## Request contract

```json
{
  "clientId": "customer.get",
  "json": { "urlId": "1" },
  "encryptedPayload": "<base64 of nonce(12) | ciphertext | tag(16)>"
}
```

`encryptedPayload` is required only for operations with `RequiresEncryption`, and rejected for all others. See DESIGN-NOTES, section 3.

## Tests

```bash
dotnet test
```

47 tests, covering all 17 required scenarios. The mapping from requirement to test is in DESIGN-NOTES, section 10.

| Folder | What it tests | Needs |
| --- | --- | --- |
| `UnitTest/ProxyServiceTests` | The request pipeline, with a fake repository, a recording destination and a fake auditor | nothing |
| `UnitTest/HttpUpstreamForwarderTests` | The real forwarder and `HttpClient`, with a stub network handler: timeout, cancellation, connection failure, response size | nothing |
| `UnitTest/PayloadDecryptorTests` | AES-256-GCM decryption, tampering, wrong key, invalid input | nothing |
| `Integration/OperationAdminStoreTests` | Optimistic concurrency against a real SQL Server in a container (Testcontainers) | Docker |

The integration tests start a SQL Server container themselves; no database setup is needed. The first run downloads the image, which takes a few minutes.

To run without Docker:

```bash
dotnet test --filter "FullyQualifiedName!~Integration"
```

## Project structure

```
src/
  SecureProxy.Domain          Entities and enums
  SecureProxy.Application     ProxyService, ProxySettings and abstractions
  SecureProxy.Infrastructure  EF Core, AES-GCM, key provider, HTTP forwarder, audit
  SecureProxy.Api             Controllers, DI, exception handler
tests/
  SecureProxy.Tests           Unit tests and integration tests
```
