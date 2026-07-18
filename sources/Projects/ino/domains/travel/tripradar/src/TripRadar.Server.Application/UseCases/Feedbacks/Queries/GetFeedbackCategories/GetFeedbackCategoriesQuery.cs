using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetFeedbackCategories;

public record GetFeedbackCategoriesQuery : IRequest<Result<IEnumerable<FeedbackCategory>>>;
