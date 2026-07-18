using TripRadar.Server.Comms.Core.Exceptions;

namespace TripRadar.Server.API.GraphQL.Queries;

public abstract class BaseQuery
{
    protected static async Task<TResponse> ExecuteQueryAsync<TQueryResult, TResponse>(
        Func<Task<TripRadar.Server.Comms.Core.SharedKernel.Result<TQueryResult>>> execute,
        Func<TQueryResult, TResponse> mapResponse)
    {
        try
        {
            var response = await execute();
            return response.IsFailure ? throw CreateGraphQlException(response.Error) : mapResponse(response.Value!);
        }
        catch (InvalidRequestException ex)
        {
            throw CreateGraphQlException(ex.Message, ex.ErrorCode);
        }
        catch (InternalErrorException ex)
        {
            throw CreateGraphQlException(ex.ErrorReason, ex.ErrorCode);
        }
    }

    private static GraphQLException CreateGraphQlException(TripRadar.Server.Comms.Core.Errors.Error error) =>
        CreateGraphQlException(error.Reason, error.Code);

    private static GraphQLException CreateGraphQlException(string message, string errorCode) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(errorCode).Build());
}
