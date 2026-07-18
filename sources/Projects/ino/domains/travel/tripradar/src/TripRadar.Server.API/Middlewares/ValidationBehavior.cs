using FluentValidation;
using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.Errors;
using Error = TripRadar.Server.Comms.Core.Errors.Error;
using SharedKernelResult = TripRadar.Server.Comms.Core.SharedKernel.Result;

namespace TripRadar.Server.API.Middlewares;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    IEnumerable<ICustomRequestValidator<TRequest>> customValidators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        foreach (var customValidator in customValidators)
        {
            var error = await customValidator.ValidateAsync(request, cancellationToken);
            if (error is not null)
                return CreateFailureResponse(error);
        }

        if (!validators.Any())
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var validationFailures = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var errors = validationFailures
            .Where(validationResult => !validationResult.IsValid)
            .SelectMany(validationResult => validationResult.Errors)
            .Select(validationFailure => new ValidationError(
                validationFailure.PropertyName,
                validationFailure.ErrorMessage))
            .ToList();

        if (errors.Count != 0)
        {
            var validationError = new Error("VALIDATION_ERROR", string.Join("; ", errors.Select(e => e.ErrorMessage)));

            return CreateFailureResponse(validationError);
        }

        var response = await next(cancellationToken);

        return response;
    }

    private static TResponse CreateFailureResponse(Error error)
    {
        if (!typeof(TResponse).IsGenericType || typeof(TResponse).GetGenericTypeDefinition() !=
            typeof(Comms.Core.SharedKernel.Result<>))
        {
            return (TResponse)(object)SharedKernelResult.Failure(error);
        }

        var resultType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(SharedKernelResult)
            .GetMethods()
            .Where(m => m is { Name: "Failure", IsGenericMethodDefinition: true })
            .FirstOrDefault(m =>
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Error))!
            .MakeGenericMethod(resultType);
        return (TResponse)failureMethod.Invoke(null, [error])!;
    }
}
