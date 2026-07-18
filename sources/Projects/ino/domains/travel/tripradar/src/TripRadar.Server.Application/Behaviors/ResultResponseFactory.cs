using System.Linq.Expressions;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Behaviors;

internal static class ResultResponseFactory<TResponse>
{
    private static readonly Func<Error, TResponse> _failureFactory = CreateFailureFactory();

    public static TResponse CreateFailure(Error error) => _failureFactory(error);

    private static Func<Error, TResponse> CreateFailureFactory()
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            return error => (TResponse)(object)Result.Failure(error);
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            return _ => throw new InvalidOperationException(
                $"Cannot create failure response for type {responseType.Name}. Expected Result or Result<T>.");
        }

        var innerType = responseType.GetGenericArguments()[0];
        var failureMethod = typeof(Result)
            .GetMethods()
            .First(method => method is { Name: nameof(Result.Failure), IsGenericMethod: true } && method.GetParameters().Length == 1)
            .MakeGenericMethod(innerType);

        var errorParameter = Expression.Parameter(typeof(Error), "error");
        var call = Expression.Call(failureMethod, errorParameter);
        var cast = Expression.Convert(call, responseType);
        return Expression.Lambda<Func<Error, TResponse>>(cast, errorParameter).Compile();
    }
}
