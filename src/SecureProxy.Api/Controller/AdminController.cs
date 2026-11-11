using Microsoft.AspNetCore.Mvc;
using SecureProxy.Api.Contracts;
using SecureProxy.Application.Abstractions;
using SecureProxy.Application.Admin;

namespace SecureProxy.Api.Controller
{
    // Configuration updates with optimistic concurrency (assignment section 9, test 16).
    // In production this endpoint must require admin authentication (out of scope here).
    [ApiController]
    [Route("api/admin/operations")]
    public class AdminController : ControllerBase
    {
        private readonly IOperationAdminStore _operationAdminStore;

        public AdminController(IOperationAdminStore operationAdminStore)
        {
            _operationAdminStore = operationAdminStore;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, UpdateOperationRequest request, CancellationToken cancellationToken)
        {
            byte[] expectedRowVersion;
            try
            {
                expectedRowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "invalid_request", correlationId = HttpContext.TraceIdentifier });
            }

            var update = new OperationUpdate(request.IsActive, request.MaxRequestSizeBytes, expectedRowVersion);

            var result = await _operationAdminStore.UpdateAsync(id, update, cancellationToken);

            return result.Status switch
            {
                // The client needs the NEW version for its next update.
                OperationUpdateStatus.Updated => Ok(new { rowVersion = Convert.ToBase64String(result.NewRowVersion!) }),
                OperationUpdateStatus.NotFound => NotFound(new { error = "operation_not_found", correlationId = HttpContext.TraceIdentifier }),
                OperationUpdateStatus.Conflict => Conflict(new { error = "concurrency_conflict", correlationId = HttpContext.TraceIdentifier }),
                _ => throw new InvalidOperationException($"Unknown update status: {result.Status}")
            };
        }
    }
}
