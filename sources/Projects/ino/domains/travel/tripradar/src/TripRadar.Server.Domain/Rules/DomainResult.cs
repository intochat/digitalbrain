using System.Diagnostics.CodeAnalysis;

namespace TripRadar.Server.Domain.Rules;

public class DomainResult
{
    internal DomainResult(bool isSuccess, DomainError error)
    {
        if (isSuccess == (error != DomainError.None))
        {
            throw new InvalidOperationException();
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    public static DomainResult Success() => new(true, DomainError.None);

    public static DomainResult Failure(DomainError error) => new(false, error);

    public static DomainResult<TValue> Success<TValue>(TValue value) => new(value, true, DomainError.None);

    public static DomainResult<TValue> Failure<TValue>(DomainError error) => new(false, error);
}

public sealed class DomainResult<TValue> : DomainResult
{
    internal DomainResult(TValue value, bool isSuccess, DomainError error) : base(isSuccess, error)
    {
        Value = value;
    }

    internal DomainResult(bool isSuccess, DomainError error) : base(isSuccess, error)
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
