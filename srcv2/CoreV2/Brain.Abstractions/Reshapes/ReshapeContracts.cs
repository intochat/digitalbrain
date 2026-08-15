using Brain.Abstractions.Events;

namespace Brain.Abstractions.Reshapes;

public interface IReshape<TFrom, TTo>
    where TFrom : IDomainEvent
    where TTo : IDomainEvent
{
    TTo Transform(TFrom source);
}
