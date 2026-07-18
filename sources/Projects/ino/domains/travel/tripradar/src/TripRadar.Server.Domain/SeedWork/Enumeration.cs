using System.Reflection;

namespace TripRadar.Server.Domain.SeedWork;

public abstract class Enumeration(int id, string name) : IComparable
{
    public string Name { get; } = name;

    public int Id { get; } = id;

    public int CompareTo(object? other)
    {
        if (other is not Enumeration otherEnumeration)
        {
            throw new ArgumentException($"Object must be of type {nameof(Enumeration)}.", nameof(other));
        }

        return Id.CompareTo(otherEnumeration.Id);
    }

    public override string ToString() => Name;

    public static IEnumerable<T> GetAll<T>() where T : Enumeration
    {
        return typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(f => f.GetValue(null)).Cast<T>();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Enumeration otherValue)
        {
            return false;
        }

        var typeMatches = GetType() == obj.GetType();
        var valueMatches = Id.Equals(otherValue.Id);

        return typeMatches && valueMatches;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Id);
    }
}
