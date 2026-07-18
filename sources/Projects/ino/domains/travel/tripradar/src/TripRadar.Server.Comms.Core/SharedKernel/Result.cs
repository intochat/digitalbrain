using System.Diagnostics.CodeAnalysis;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Comms.Core.SharedKernel;

public class Result
{
    internal Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
            case false when error == Error.None:
                throw new InvalidOperationException();
            default:
                IsSuccess = isSuccess;
                Error = error;
                break;
        }
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(false, error);
}

public sealed class Result<TValue> : Result
{
    internal Result(TValue value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        Value = value;
    }

    internal Result(bool isSuccess, Error error) : base(isSuccess, error)
    {
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public new bool IsSuccess => base.IsSuccess;

    [MemberNotNullWhen(false, nameof(Value))]
    public new bool IsFailure => base.IsFailure;

    public TValue? Value
    {
        get
        {
            if (!IsSuccess)
            {
                throw new InvalidOperationException("The value of a failure result can not be accessed.");
            }

            return field;
        }
    }
}
