using System.Text;
using DigitalBrain.Rules;

namespace DigitalBrain.Modules.Validation;

// The rule name is "code:path"; ModuleValidationException splits it back into an issue.
// Messages describe the rule without echoing input.

/// <summary>A value is required: not null, and not blank when text.</summary>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
public sealed record Required(string Path, object? Value) : Rule($"required:{Path}")
{
    /// <inheritdoc />
    public override bool Broken => Value is null || Value is string text && string.IsNullOrWhiteSpace(text);

    /// <inheritdoc />
    public override string Message => "A value is required.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"Required {{ Name = {Name}, Path = {Path} }}";
}

/// <summary>Text length in Unicode scalar values is within bounds; null passes.</summary>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
/// <param name="Minimum">The fewest scalar values.</param>
/// <param name="Maximum">The most scalar values.</param>
public sealed record StringLength(string Path, string? Value, int Minimum, int Maximum) : Rule($"length:{Path}")
{
    /// <inheritdoc />
    public override bool Broken
    {
        get
        {
            Bounds.Check(Minimum, Maximum);
            if (Value is null) { return false; }
            var length = Value.EnumerateRunes().Count();
            return length < Minimum || length > Maximum;
        }
    }

    /// <inheritdoc />
    public override string Message => $"Use between {Minimum} and {Maximum} characters.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"StringLength {{ Name = {Name}, Path = {Path}, Minimum = {Minimum}, Maximum = {Maximum} }}";
}

/// <summary>Text size in UTF-8 bytes is at most the maximum; null passes.</summary>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
/// <param name="MaximumBytes">The most bytes.</param>
public sealed record Utf8Length(string Path, string? Value, int MaximumBytes) : Rule($"utf8_bytes:{Path}")
{
    /// <inheritdoc />
    public override bool Broken
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(MaximumBytes);
            return Value is not null && Encoding.UTF8.GetByteCount(Value) > MaximumBytes;
        }
    }

    /// <inheritdoc />
    public override string Message => $"Use at most {MaximumBytes} UTF-8 bytes.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"Utf8Length {{ Name = {Name}, Path = {Path}, MaximumBytes = {MaximumBytes} }}";
}

/// <summary>An array's item count is within bounds; null passes.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
/// <param name="Minimum">The fewest items.</param>
/// <param name="Maximum">The most items.</param>
public sealed record ArrayLength<T>(string Path, T[]? Value, int Minimum, int Maximum) : Rule($"length:{Path}")
{
    /// <inheritdoc />
    public override bool Broken
    {
        get
        {
            Bounds.Check(Minimum, Maximum);
            return Value is not null && (Value.Length < Minimum || Value.Length > Maximum);
        }
    }

    /// <inheritdoc />
    public override string Message => $"Use between {Minimum} and {Maximum} items.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"ArrayLength {{ Name = {Name}, Path = {Path}, Minimum = {Minimum}, Maximum = {Maximum} }}";
}

/// <summary>A number is within bounds, inclusive.</summary>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
/// <param name="Minimum">The lowest allowed value.</param>
/// <param name="Maximum">The highest allowed value.</param>
public sealed record NumberRange(string Path, decimal Value, decimal Minimum, decimal Maximum) : Rule($"range:{Path}")
{
    /// <inheritdoc />
    public override bool Broken
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(Maximum, Minimum);
            return Value < Minimum || Value > Maximum;
        }
    }

    /// <inheritdoc />
    public override string Message => $"Use a value between {Minimum} and {Maximum}.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"NumberRange {{ Name = {Name}, Path = {Path}, Minimum = {Minimum}, Maximum = {Maximum} }}";
}

/// <summary>A URI is absolute with an http or https scheme; null passes.</summary>
/// <param name="Path">The option path.</param>
/// <param name="Value">The option value.</param>
public sealed record HttpUri(string Path, Uri? Value) : Rule($"uri:{Path}")
{
    /// <inheritdoc />
    public override bool Broken => Value is not null && (!Value.IsAbsoluteUri || Value.Scheme is not ("http" or "https"));

    /// <inheritdoc />
    public override string Message => "Use an absolute HTTP or HTTPS URI.";

    /// <summary>Names the rule and its path; never the value, which may be a secret.</summary>
    public override string ToString() => $"HttpUri {{ Name = {Name}, Path = {Path} }}";
}

internal static class Bounds
{
    internal static void Check(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
    }
}
