using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IRefundService
{
    /// <summary>
    /// Creates a refund for the specified user.
    /// </summary>
    /// <param name="user">The user requesting the refund.</param>
    /// <param name="type">The type/reason for the refund.</param>
    /// <param name="metadata">Optional metadata to attach to the refund.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing refund details or an error.</returns>
    Task<Result<RefundResult>> CreateRefundAsync(User user, RefundType type, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
}
