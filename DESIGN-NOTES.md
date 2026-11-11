# Design Notes: Secure Proxy Web API

## 1. Overview

The proxy forwards only explicitly configured operations to predefined destination APIs. The caller picks an operation by key. The destination URL, HTTP method and path always come from trusted configuration in SQL Server, never from the request.

The guiding rule is **fail closed**: every validation and policy check runs before forwarding. If any check fails, the destination receives nothing. Tests prove this by asserting that a recording destination received no request; a status code alone is not treated as proof.

## 2. Architecture

```
Api  ──►  Infrastructure  ──►  Application  ──►  Domain
```

| Project | Responsibility |
| --- | --- |
| `SecureProxy.Domain` | Entities and enums. No dependencies. |
| `SecureProxy.Application` | `ProxyService` (the request pipeline), `ProxySettings`, and the abstractions it needs: `IOperationRepository`, `IPayloadDecryptor`, `IUpstreamForwarder`, `IEncryptionKeyProvider`, `IRequestAuditor`, `IOperationAdminStore`. |
| `SecureProxy.Infrastructure` | Implementations: EF Core (`ProxyDbContext`, `OperationRepository`, `OperationAdminStore`, `RequestAuditor`), AES-GCM (`PayloadDecryptor`), key source (`ConfigurationKeyProvider`), HTTP (`HttpUpstreamForwarder`). |
| `SecureProxy.Api` | `ProxyController`, `AdminController`, DI wiring, global exception handler. |

`ProxyService` has no dependency on HTTP, EF Core or cryptography. It depends only on interfaces, so it is unit-tested with fakes.

## 3. Request contract

The example in the assignment is incomplete, and the recruiter confirmed the data is open to interpretation. The contract I chose:

```json
POST /api/proxy/execute
{
  "clientId": "customer.get",
  "json": { "urlId": "12345" },
  "encryptedPayload": "<base64>"
}
```

| Field | Meaning |
| --- | --- |
| `clientId` | The operation key, mapped to `Operation.Key`. Section 2 says "the caller requests an operation by its configured key", and the assignment's own example uses `"clientId": "customer.get"`. It is not a caller identity. |
| `json` | Caller parameters. Values are strings; each `ParameterRule` validates the type. |
| `json.urlId` | The resource identifier that fills the `{urlId}` placeholder in the operation's path template. |
| `encryptedPayload` | Optional. Required only when the operation has `RequiresEncryption`. |

**Deviation from the text.** The assignment says "Json must contain the urlId key". I require `urlId` only when the path template contains `{urlId}`, and I **reject** it when it does not. A field that is required but meaningless for some operations would be silently ignored, and silently ignoring input is the opposite of secure by default. I rejected the alternative reading, `urlId` as "which path variant to call", because it would let the caller choose the destination path, which section 2 forbids.

## 4. Processing pipeline

Checks run cheapest first, and each failure returns immediately.

| # | Step | On failure |
| --- | --- | --- |
| 1 | Envelope: `clientId` present | 400 `invalid_request` |
| 2 | Operation found by key | 404 `operation_not_found` |
| 3 | Operation **and** destination active | 404 `operation_not_found` (same as step 2, on purpose) |
| 4 | `urlId` present exactly when the path has `{urlId}` | 400 `invalid_parameters` |
| 5 | Each `ParameterRule`: required, max length, data type, regex pattern, path safety | 400 `invalid_parameters` |
| 6 | Unknown parameters, unless the operation allows them | 400 `invalid_parameters` |
| 7 | Encryption policy and AES-GCM decryption | 400 `invalid_encrypted_payload` |
| 8 | Decrypted body size against `Operation.MaxRequestSizeBytes` | 413 `request_too_large` |
| 9 | Allowed headers, URL from configuration, forward | 504 `upstream_timeout`, 502 `upstream_unavailable` |

## 5. Security decisions

**Configuration-driven forwarding.** The upstream request is built from configuration, not from input. Input only fills slots that the configuration defines. The query string contains only parameters that have a rule with `Location = Query`. Even with `AllowUnknownParameters = true`, an unknown parameter is never forwarded. Tests 5 and 6 prove that a caller-supplied URL or method has no effect.

**No operation enumeration.** Unknown operations, disabled operations and disabled destinations all return the same 404 `operation_not_found`. Otherwise an attacker trying names could learn which operations exist, even disabled ones. The exact reason (`unknown_operation`, `operation_disabled`, `destination_disabled`) is kept in the audit log, so nothing is lost operationally.

**Non-sensitive errors.** Error bodies contain only a stable code and a correlation ID: `{ "error": "invalid_parameters", "correlationId": "..." }`. They never include the failing parameter name, hostnames, encryption details or stack traces. A legitimate caller who is stuck can report the correlation ID, and the audit row has the exact reason.

**Path traversal, defended in two layers.**
1. Validation rejects `Path` parameter values that contain `/` or `\`, or that equal `.` or `..`.
2. URL building always applies `Uri.EscapeDataString`, so encoded input such as `..%2F` or `1?admin=true` stays a single path segment (`..%252F`, `1%3Fadmin%3Dtrue`). This also prevents query-string injection through path values.

**ReDoS.** Parameter patterns run with a 100 ms regex timeout.

**Headers (allowlist).** The proxy builds a new upstream request; it never copies the incoming headers.
- Only headers named in `Proxy:AllowedHeaders` are forwarded, for example `Idempotency-Key` and `X-Request-Id`. Header names are compared case-insensitively.
- A hard-coded deny set (`Authorization`, `Cookie`, `Host` and the hop-by-hop headers) is never forwarded, even if someone adds one of these to the allowlist by mistake. This is defense in depth; test 10 includes `Authorization` in the test allowlist on purpose to prove it.
- The caller's `Authorization` authenticates the caller to the proxy, not to the destination. If a destination requires authentication, the proxy should use its own credentials for that destination: an authentication profile reference per destination (section 5.1), with the secret in the key store. If the destination needs to know who the caller is, the proxy adds the verified identity (for example `X-Caller-Id`), never the caller's credential. Not implemented; see section 12.
- The allowlist is global. An allowlist per destination or per operation (a `HeaderRules` table) is the natural next step.

**Unexpected errors.** A global exception handler, first in the pipeline, turns any unhandled exception into `500 internal_error` with a correlation ID. Stack traces stay in the server log.

**Proxy vs upstream responses.** Every proxy response carries `X-Proxy-Source: proxy` or `X-Proxy-Source: upstream`. An upstream 404 or 500 is passed through unchanged (status, body and content type) and marked `upstream`, so the caller can tell it apart from a proxy rejection.

## 6. Encryption

- **Algorithm:** AES-256-GCM. The tag authenticates the nonce and the ciphertext, so any modification, even a single bit, fails decryption.
- **Payload format:** Base64 of `nonce (12 bytes) | ciphertext | tag (16 bytes)`. Minimum length is 28 bytes; an empty ciphertext is valid (a boundary test covers it).
- **Key source:** the database stores only `EncryptionProfile.KeyReference`, the name of the secret. `IEncryptionKeyProvider` resolves it. Locally the key comes from User Secrets or an environment variable (`ConfigurationKeyProvider`); in production the same interface would be backed by Azure Key Vault.
- **Profiles:** one key per partner. `Name` is a stable human label (`courier-key`); `KeyReference` is the technical location, which changes on key rotation (`ENCRYPTION_KEY_COURIER_2026` → `_2027`) without changing the profile's identity.

**Client errors vs server errors.**

| Cause | Result |
| --- | --- |
| Bad Base64, payload too short, tag mismatch, wrong key | `TryDecrypt` returns false → 400 |
| Key missing, not Base64, or not 32 bytes | Exception → 500 and a log entry |

`PayloadDecryptor` catches only `FormatException` and `AuthenticationTagMismatchException`. Catching `Exception` would turn a broken key configuration into "client sent a bad payload", which hides an outage behind 400s. For the same reason, `ConfigurationKeyProvider` rethrows a malformed key as `InvalidOperationException`; a `FormatException` from the key would otherwise be caught by the decryptor and reported as a client error.

**Encryption policy per operation:**

| `RequiresEncryption` | Payload | Result |
| --- | --- | --- |
| true | missing or invalid | 400 |
| true | valid | decrypted body is forwarded |
| true | (operation has no profile) | 500, configuration error |
| false | present | 400; unexpected input is not ignored |

## 7. Data model

| Table | Notes |
| --- | --- |
| `Destinations` | Unique `Name`; `TimeoutSeconds`. |
| `Operations` | Unique `Key`; `RowVersion` (SQL Server `rowversion`); FK to destination with `Restrict` (a destination that still has operations cannot be deleted). |
| `ParameterRules` | Unique `(OperationId, Name)`; enums stored as text; `Cascade` from operation. |
| `EncryptionProfiles` | `Name` (unique) and `KeyReference`; never the key. |
| `RequestLogs` | One row per proxy request; see section 9. |

- Every string column has an explicit max length.
- All configuration entities implement `IAuditable`. `ProxyDbContext` sets `CreatedAt` and `UpdatedAt` (UTC) in `SaveChanges`, and prevents `CreatedAt` from being overwritten. This covers "when was configuration changed" from section 5.7; a full history is described in section 9.
- The Domain project has no EF attributes. `rowversion`, indexes and delete behaviors are configured with the Fluent API in Infrastructure.

## 8. Reliability and performance

**HTTP client lifecycle.** `HttpUpstreamForwarder` is a typed client registered with `AddHttpClient`. `IHttpClientFactory` pools and recycles handlers. That avoids socket exhaustion from creating a client per request, and stale DNS from a single static client.

**Timeouts and cancellation.**
- The forwarder links the caller's `CancellationToken` with a timer set to the destination's `TimeoutSeconds`.
- `HttpClient.Timeout` is set to infinite, so only these two sources can cancel.
- On `OperationCanceledException`: if the caller's token was **not** cancelled, our timer fired, and the proxy returns 504. If the caller cancelled, the exception propagates: there is no one to respond to, and work stops early. The audit row records `Cancelled`.
- `HttpRequestException` (DNS, refused connection, TLS) returns 502.

**Size limits.**
- Request: two layers. `[RequestSizeLimit(1 MB)]` on the endpoint rejects oversized requests before the body is read. `MaxRequestSizeBytes` per operation is enforced on the decrypted body.
- Response: `Proxy:MaxResponse` (default 1 MB). The forwarder uses `HttpCompletionOption.ResponseHeadersRead`, so the body is not buffered by `SendAsync`. It rejects early when `Content-Length` is over the limit, and otherwise reads the stream in 8 KB chunks while counting, stopping as soon as the limit is passed. This also covers responses without `Content-Length` (chunked) or with a wrong one. An oversized response returns 502 to the caller, and `upstream_response_too_large` in the audit.
- Trade-off: the accepted body is buffered (bounded by the limit) instead of streamed to the caller. Buffering lets the proxy return a clean error before anything is sent; once streaming to the caller starts, the status code is already committed. With small JSON APIs and an enforced limit, bounded buffering is the safer choice.

**Retries and idempotency.** The proxy does not retry.
- Safe to retry: idempotent methods (GET, HEAD, PUT, DELETE by HTTP semantics) when the failure happened before a response, such as a connection failure or a 503.
- Never retry automatically: POST, or any failure after the upstream may have processed the request, such as a timeout. The work may have happened.
- For POST: the caller sends an `Idempotency-Key`, which the proxy forwards (it is in the header allowlist). The upstream stores the result per key and returns the stored result on a repeat. The proxy should not invent the key, because only the caller knows whether two requests are the same intent.

**Configuration caching.** Not implemented; every request reads the operation from SQL Server (one query, `AsNoTracking`).
- Proposed design: `IMemoryCache` keyed by operation key, with a short absolute expiration (for example 30 seconds), and removal of the entry when configuration is updated through the admin endpoint.
- Consistency model: a change takes effect immediately on the instance that handled the update, and within the TTL on other instances.
- Disabling is the case that matters most; a short TTL bounds how long a disabled operation can still be used.

**Concurrency (configuration updates).** `PUT /api/admin/operations/{id}` uses optimistic concurrency with the `rowversion` column.
- The client sends the `rowVersion` it read (Base64). The store loads the operation by id (404 if missing), returns 409 if the stored version already differs, then sets the client's version as EF's *original value* so that the `UPDATE ... WHERE Id = @id AND RowVersion = @expected` checks and writes in one statement. A change that lands between load and save makes that statement match zero rows, EF throws `DbUpdateConcurrencyException`, and the endpoint returns 409.
- The explicit version check before saving is needed because EF sends no `UPDATE` at all when no value changed; without it, a stale request with unchanged values would get 200. This was found in manual testing, and a test now covers it.
- A 409 is not retried automatically: the admin must reload, see the current state (for example, that the operation was disabled), and decide again. An automatic retry would overwrite the other change, which is exactly the lost update this prevents.
- In production this endpoint must require admin authentication (out of scope here).

**Requests in flight when an operation is disabled.** A request that already passed step 3 completes. Disabling affects new requests only. Cancelling in-flight upstream calls could leave the upstream in an unknown state, which is worse than letting one request finish.

**Resilience.** Not implemented.
- Proposed: ASP.NET Core rate limiting per caller (a fixed or sliding window).
- A concurrency limit per destination (bulkhead), so one slow destination cannot use every thread.
- A circuit breaker per destination (`Microsoft.Extensions.Http.Resilience`), which returns 502 immediately while a destination is failing.
- No retry policy is attached to non-idempotent operations.

## 9. Audit and logging

**One `RequestLogs` row per proxy request** (section 5.6): correlation ID, operation key, destination, start and completion time, duration, whether the request was forwarded, upstream status, proxy status, outcome (`Forwarded`, `Rejected`, `UpstreamTimeout`, `UpstreamUnavailable`, `Cancelled`, `Failed`) and an internal reason (for example `operation_disabled`, `unknown_operation`, `upstream_response_too_large`).

- `ProxyService.ExecuteAsync` wraps the pipeline; a `finally` block writes the row, so every return and every exception produces exactly one row.
- The reason is internal only. The caller sees a generic code; the audit sees why.
- Never stored or logged: keys, authorization headers, cookies, request or response bodies.

**Audit policy if SQL Server is down: best effort.** If the audit write fails, the request still completes, and the full entry is written to the structured log instead (`ILogger` at Error level). The audit write uses `CancellationToken.None`, so a cancelled request still gets its row.
- Why: the proxy is the gateway to every destination. Failing all traffic because the audit table is unavailable is a larger outage than a temporary gap in the audit table, and the structured log keeps the information.
- Trade-off: during a database outage, audit rows are missing from SQL and exist only in logs.
- Alternatives: *mandatory* (reject requests when the audit cannot be written) is right for regulated environments such as banking, at the cost of availability; *buffered* (queue in memory, write later) loses entries if the process crashes and adds complexity.

**Configuration history (5.7), partial.** `CreatedAt` and `UpdatedAt` are maintained automatically. A full history is not implemented. Proposed: a `ConfigurationChanges` table written in `SaveChanges` from the change tracker, recording the entity, id, changed properties with old and new values, the timestamp and the user. `KeyReference` values may be recorded, since they are names, not secrets; key material never touches the database.

## 10. Testing

47 tests. `dotnet test` runs them all; the four integration tests need Docker.

| Level | What is real | What is fake |
| --- | --- | --- |
| `ProxyServiceTests` | the pipeline | repository, forwarder (`RecordingForwarder`), auditor |
| `HttpUpstreamForwarderTests` | the forwarder and `HttpClient` | the network (`StubHttpMessageHandler`) |
| `PayloadDecryptorTests` | AES-GCM decryption | the key provider |
| `OperationAdminStoreTests` | EF Core and SQL Server (Testcontainers) | nothing |

Rejection tests assert that the fake destination received **nothing**, as section 12 requires.

| # | Requirement | Test |
| --- | --- | --- |
| 1 | Valid operation is forwarded | `ProxyServiceTests.ExecuteAsync_ValidOperation_IsForwarded` |
| 2 | Unknown operation is rejected | `ExecuteAsync_UnknownOperation_IsRejectedAndNotForwarded` |
| 3 | Disabled operation is rejected | `ExecuteAsync_DisabledOperation_IsRejectedAndNotForwarded` |
| 4 | Disabled destination is rejected | `ExecuteAsync_DisabledDestination_IsRejectedAndNotForwarded` |
| 5 | Caller URL cannot override configuration | `ExecuteAsync_CallerUrlIgnored_ForwardsToConfiguredDestination` |
| 6 | Invalid or disallowed method is rejected | `ExecuteAsync_CallerMethodIgnored_ForwardsWithConfiguredMethod` (the caller cannot choose a method; one supplied in the payload has no effect) |
| 7 | Missing required parameters are rejected | `ExecuteAsync_MissingRequiredParameter_IsRejectedAndNotForwarded` |
| 8 | Unknown parameters follow the operation's policy | `ExecuteAsync_UnknownParameterNotAllowed_...`, `ExecuteAsync_UnknownParameterAllowed_IsForwardedWithoutIt` |
| 9 | Path traversal is rejected | `ExecuteAsync_PathTraversal_IsRejectedAndNotForwarded` (4 cases) |
| 10 | Disallowed headers are not forwarded | `ExecuteAsync_DisallowedHeaders_AreNotForwarded` |
| 11 | Invalid AES-GCM tag is rejected | `ExecuteAsync_TamperedEncryptedPayload_...`, plus `PayloadDecryptorTests` |
| 12 | Invalid encrypted payload is never forwarded | `ExecuteAsync_InvalidEncryptedPayload_...` (4 cases), `ExecuteAsync_PayloadEncryptedWithOtherKey_...` |
| 13 | Upstream timeout is handled | Two levels: `HttpUpstreamForwarderTests.SendAsync_UpstreamTooSlow_ReturnsTimedOut` (the real timer) and `ProxyServiceTests.ExecuteAsync_UpstreamTimeout_Returns504` (the mapping) |
| 14 | Upstream HTTP error is handled | `ExecuteAsync_UpstreamReturns500_IsPassedThroughAsUpstream`, `ExecuteAsync_UpstreamConnectionFailed_Returns502` |
| 15 | Cancellation is propagated | `HttpUpstreamForwarderTests.SendAsync_CallerCancels_ThrowsAndDoesNotReportTimeout` |
| 16 | Configuration concurrency conflict is handled | `OperationAdminStoreTests.UpdateAsync_TwoAdminsSameVersion_SecondGetsConflict` (real SQL Server), plus the stale-version and not-found cases |
| 17 | Audit records for success and rejection | `ExecuteAsync_SuccessfulRequest_WritesAuditRow`, `ExecuteAsync_RejectedRequest_WritesAuditRowWithReason`, `ExecuteAsync_DisabledOperation_AuditReasonDiffersFromUnknown` |

## 11. Scope notes

- **Section 13** (existing repository and defect report) was excluded by the recruiter on 9.11. The solution was built from scratch; `DEFECT-REPORT.md` records this.
- **Development seed data** points to JSONPlaceholder (a public fake API), so the proxy can be tried manually. The seed runs only in Development, and is skipped if data exists.

## 12. With more time

- Configuration caching with explicit invalidation.
- Rate limiting, bulkheads and circuit breakers per destination.
- Streaming the accepted response body to the caller, with byte counting.
- Full configuration history.
- Authenticating callers (out of scope here), so that permissions and keys can be tied to a caller identity, and protecting the admin endpoint.
- Per-destination authentication profiles: the proxy authenticates to each destination with its own credentials, and forwards the verified caller identity instead of the caller's token.
- Header rules per destination or per operation, instead of one global allowlist.
- A specific audit reason for each parameter failure (missing, type, pattern, path, unknown); today all are `invalid_parameters`.
