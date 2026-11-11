using Microsoft.EntityFrameworkCore;
using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Admin;

namespace SecureProxy.Infrastructure.Persistence
{
    public class OperationAdminStore : IOperationAdminStore
    {
        private readonly ProxyDbContext _dbContext;

        public OperationAdminStore(ProxyDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<OperationUpdateResult> UpdateAsync(int operationId, OperationUpdate update, CancellationToken cancellationToken)
        {
            // 1. Find by id ONLY (tracked, because we are going to change it). null = really does not exist.
            var operation = await _dbContext.Operations
                .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);

            if (operation == null)
            {
                return new OperationUpdateResult(OperationUpdateStatus.NotFound);
            }

            // 2a. Stale version already visible now -> Conflict.
            //     Needed because EF sends no UPDATE at all when no value changed,
            //     and then the WHERE check below would never run.
            if (!operation.RowVersion.SequenceEqual(update.ExpectedRowVersion))
            {
                return new OperationUpdateResult(OperationUpdateStatus.Conflict);
            }

            // 2b. Compare against the version the CLIENT saw, not the one we just loaded.
            //    EF puts this value in the WHERE of the UPDATE, so check and update happen in one statement.
            //    This catches a change that happens AFTER the check above (between load and save).
            _dbContext.Entry(operation).Property(o => o.RowVersion).OriginalValue = update.ExpectedRowVersion;

            // 3. Apply the changes.
            operation.IsActive = update.IsActive;
            operation.MaxRequestSizeBytes = update.MaxRequestSizeBytes;

            try
            {
                // 4. UPDATE ... WHERE Id = @id AND RowVersion = @expected.
                //    Goes through SaveChanges, so UpdatedAt (IAuditable) is set automatically.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // 0 rows matched: someone changed the operation after the client read it.
                return new OperationUpdateResult(OperationUpdateStatus.Conflict);
            }

            // After SaveChanges, EF has read back the NEW rowversion that SQL Server generated.
            return new OperationUpdateResult(OperationUpdateStatus.Updated, operation.RowVersion);
        }
    }
}
