namespace TripRadar.Server.Domain.SeedWork;

public abstract class Entity<TId>()
{
    private int? _requestedHashCode;

    public TId Id { get; protected set; } = default!;

    private bool IsTransient() => EqualityComparer<TId>.Default.Equals(Id, default!);

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        if (other.IsTransient() || IsTransient())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode()
    {
        if (IsTransient())
        {
            return base.GetHashCode();
        }

        _requestedHashCode ??= Id?.GetHashCode() is int idHash ? (idHash ^ 31) : base.GetHashCode();
        return _requestedHashCode.Value;
    }

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
