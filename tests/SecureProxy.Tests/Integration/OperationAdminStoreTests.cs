using SecureProxy.Application.Admin;
using SecureProxy.Infrastructure.Persistence;

namespace SecureProxy.Tests.Integration;

// Test 16: configuration concurrency conflicts are handled. Runs against a real SQL Server.
public class OperationAdminStoreTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _sql;

    public OperationAdminStoreTests(SqlServerFixture sql)
    {
        _sql = sql;
    }

    // Dana and Yossi both read version A. Dana saves first; Yossi's save must get Conflict,
    // and Dana's change must still be in the database.
    [Fact]
    public async Task UpdateAsync_TwoAdminsSameVersion_SecondGetsConflict()
    {
        var (operationId, versionA) = await _sql.CreateOperationAsync();

        // Two separate DbContexts = two separate admins.
        await using var danaDb = _sql.CreateDbContext();
        await using var yossiDb = _sql.CreateDbContext();
        var danaStore = new OperationAdminStore(danaDb);
        var yossiStore = new OperationAdminStore(yossiDb);

        var danaResult = await danaStore.UpdateAsync(
            operationId, new OperationUpdate(IsActive: false, MaxRequestSizeBytes: 4096, versionA), CancellationToken.None);

        var yossiResult = await yossiStore.UpdateAsync(
            operationId, new OperationUpdate(IsActive: true, MaxRequestSizeBytes: 8192, versionA), CancellationToken.None);

        Assert.Equal(OperationUpdateStatus.Updated, danaResult.Status);
        Assert.Equal(OperationUpdateStatus.Conflict, yossiResult.Status);

        // Dana's change survived: the operation is still disabled, with Dana's size.
        await using var checkDb = _sql.CreateDbContext();
        var saved = await checkDb.Operations.FindAsync(operationId);
        Assert.False(saved!.IsActive);
        Assert.Equal(4096, saved.MaxRequestSizeBytes);
    }

    // A successful update returns a NEW version, different from the one sent.
    [Fact]
    public async Task UpdateAsync_CurrentVersion_UpdatesAndReturnsNewVersion()
    {
        var (operationId, version) = await _sql.CreateOperationAsync();
        await using var db = _sql.CreateDbContext();
        var store = new OperationAdminStore(db);

        var result = await store.UpdateAsync(
            operationId, new OperationUpdate(false, 4096, version), CancellationToken.None);

        Assert.Equal(OperationUpdateStatus.Updated, result.Status);
        Assert.NotNull(result.NewRowVersion);
        Assert.False(version.SequenceEqual(result.NewRowVersion!));
    }

    // The bug found in manual testing: a stale version with UNCHANGED values must still be Conflict.
    // (EF sends no UPDATE when nothing changed, so only the in-memory version check catches it.)
    [Fact]
    public async Task UpdateAsync_StaleVersionWithNoChanges_StillConflict()
    {
        var (operationId, versionA) = await _sql.CreateOperationAsync();

        // Someone else updates first -> the version in the database is no longer A.
        await using (var otherDb = _sql.CreateDbContext())
        {
            await new OperationAdminStore(otherDb).UpdateAsync(
                operationId, new OperationUpdate(true, 8192, versionA), CancellationToken.None);
        }

        // Now send version A again, with exactly the values that are already in the database.
        await using var db = _sql.CreateDbContext();
        var result = await new OperationAdminStore(db).UpdateAsync(
            operationId, new OperationUpdate(true, 8192, versionA), CancellationToken.None);

        Assert.Equal(OperationUpdateStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        await using var db = _sql.CreateDbContext();
        var store = new OperationAdminStore(db);

        var result = await store.UpdateAsync(
            999_999, new OperationUpdate(true, 4096, new byte[8]), CancellationToken.None);

        Assert.Equal(OperationUpdateStatus.NotFound, result.Status);
    }
}
