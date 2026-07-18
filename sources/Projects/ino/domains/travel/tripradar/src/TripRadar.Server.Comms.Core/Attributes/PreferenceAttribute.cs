namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class PreferenceAttribute(string preferenceName, string? propertyPath = null) : Attribute
{
    public string PreferenceName { get; } = preferenceName;
    public string? PropertyPath { get; } = propertyPath;
}
