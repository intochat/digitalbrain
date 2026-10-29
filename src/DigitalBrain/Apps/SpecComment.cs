// One spec-comment extractor for the runtime, the catalogue, the baseline test and the analyzers (08-CONTEXT D-02,
// RESEARCH pitfall 10). This file is compiled into DigitalBrain (public) and linked into DigitalBrain.Analyzers
// (internal, DIGITALBRAIN_ANALYZERS) so the analyzer has no project reference; keep it netstandard2.0-safe and
// dependency-free: no records, ranges, spans or BCL members newer than netstandard2.0.
#pragma warning disable CA1510, CA1865, CA1866, CA1867, CA1847, CA1307, CA1309, CA1310 // netstandard2.0 overloads (shared with the analyzer)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace DigitalBrain.Apps;

/// <summary>Where an app sits in a brain, read from the <c>Role:</c> header line of its spec comment.</summary>
#if DIGITALBRAIN_ANALYZERS
internal
#else
public
#endif
enum AppRole
{
    /// <summary>Part of the workspace frame.</summary>
    Shell,

    /// <summary>Installed in every new brain.</summary>
    Default,

    /// <summary>Offered in Settings.</summary>
    Optional,

    /// <summary>A canonical script the fast path and templates learn from.</summary>
    Example,
}

/// <summary>A problem found while parsing a spec comment; <see cref="Line"/> is 1-based within the comment text.</summary>
#if DIGITALBRAIN_ANALYZERS
internal
#else
public
#endif
sealed class SpecCommentError
{
    /// <summary>Creates an error.</summary>
    /// <param name="line">The 1-based line within the comment text, or 0 for the whole comment.</param>
    /// <param name="message">What is wrong; names the header, never echoes the value.</param>
    public SpecCommentError(int line, string message)
    {
        Line = line;
        Message = message;
    }

    /// <summary>The 1-based line within the comment text, or 0 for the whole comment.</summary>
    public int Line { get; }

    /// <summary>What is wrong.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => Line > 0 ? "line " + Line.ToString(CultureInfo.InvariantCulture) + ": " + Message : Message;
}

/// <summary>
/// The spec comment of an app: <c># Title</c>, the header lines <c>Role:</c>, <c>Shipped:</c> and optionally
/// <c>Operations:</c>, the description and the <c>## Scenario:</c> sections (handbook app-convention).
/// </summary>
/// <remarks>
/// Untrusted input: the grammar is strict line by line, an unknown <c>Role</c> or <c>Shipped</c> value is an error
/// and never a default, and text beyond <see cref="MaxLength"/> is not read. <see cref="Role"/> never grants trust.
/// </remarks>
#if DIGITALBRAIN_ANALYZERS
internal
#else
public
#endif
sealed class SpecComment
{
    /// <summary>The longest spec comment read, in characters (64 KiB); longer text is cut and reported.</summary>
    public const int MaxLength = 64 * 1024;

    private static readonly string[] InvokeVerbs = ["invoking", "invokes", "invoke", "posts", "requests", "requesting"];

    private SpecComment(string text, string? title, string description, AppRole? role, bool? shipped, IList<string> operations,
        bool operationsDeclared, IList<string> scenarios, IList<SpecCommentError> errors)
    {
        Text = text;
        Title = title;
        Description = description;
        Role = role;
        Shipped = shipped;
        Operations = new ReadOnlyCollection<string>(operations);
        OperationsDeclared = operationsDeclared;
        Scenarios = new ReadOnlyCollection<string>(scenarios);
        Errors = new ReadOnlyCollection<SpecCommentError>(errors);
    }

    /// <summary>The comment text that was parsed (without delimiters, cut at <see cref="MaxLength"/>).</summary>
    public string Text { get; }

    /// <summary>The <c># Title</c> line without the marker, or null.</summary>
    public string? Title { get; }

    /// <summary>The prose between the header lines and the first <c>##</c> section.</summary>
    public string Description { get; }

    /// <summary>The <c>Role:</c> header, or null when absent or invalid (see <see cref="Errors"/>).</summary>
    public AppRole? Role { get; }

    /// <summary>The <c>Shipped:</c> header, or null when absent or invalid (see <see cref="Errors"/>).</summary>
    public bool? Shipped { get; }

    /// <summary>
    /// The operations the comment describes, normalized with <see cref="NormalizeOperation"/>: the
    /// <c>Operations:</c> header when present, else every name quoted after an invoke verb
    /// (<c>Invoking "add"</c>, <c>posts "clear"</c>).
    /// </summary>
    public IReadOnlyList<string> Operations { get; }

    /// <summary>True when the comment has an explicit <c>Operations:</c> header.</summary>
    public bool OperationsDeclared { get; }

    /// <summary>The titles of the <c>## Scenario:</c> sections, in order.</summary>
    public IReadOnlyList<string> Scenarios { get; }

    /// <summary>Grammar errors; empty when the comment is well formed.</summary>
    public IReadOnlyList<SpecCommentError> Errors { get; }

    /// <summary>The operation name as the runtime matches it: lower case, dashes and underscores removed.</summary>
    /// <param name="name">An operation or <c>[On]</c> method name.</param>
    public static string NormalizeOperation(string name)
    {
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        var text = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == '-' || c == '_' || char.IsWhiteSpace(c)) { continue; }
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }

    /// <summary>True when the comment describes <paramref name="operation"/> (normalized), or quotes it anywhere.</summary>
    /// <param name="operation">An operation or <c>[On]</c> method name.</param>
    public bool Describes(string operation)
    {
        var key = NormalizeOperation(operation);
        foreach (var described in Operations)
        {
            if (described == key) { return true; }
        }
        if (OperationsDeclared) { return false; }
        foreach (var quoted in Quoted(Text))
        {
            if (NormalizeOperation(quoted) == key) { return true; }
        }
        return false;
    }

    /// <summary>
    /// The leading comments of a C# source, before its first token, without their delimiters; null when there are none.
    /// Reading stops at the first token that is not a comment and after <see cref="MaxLength"/> characters.
    /// </summary>
    /// <param name="source">C# source text, or the leading trivia of a declaration.</param>
    public static string? Extract(string source)
    {
        if (source is null) { throw new ArgumentNullException(nameof(source)); }
        var lines = new List<string>();
        var i = 0;
        var read = 0;
        while (i < source.Length && read < MaxLength)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { break; }
                var body = source.Substring(i + 2, end - i - 2);
                if (body.StartsWith("*", StringComparison.Ordinal) && !body.StartsWith("**", StringComparison.Ordinal)) { body = body.Substring(1); }
                lines.Add(body);
                read += body.Length;
                i = end + 2;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                if (end < 0) { end = source.Length; }
                var line = source.Substring(i + 2, end - i - 2).TrimEnd('\r');
                if (line.StartsWith("/", StringComparison.Ordinal)) { line = line.Substring(1); }
                if (line.StartsWith(" ", StringComparison.Ordinal)) { line = line.Substring(1); }
                lines.Add(line);
                read += line.Length;
                i = end;
                continue;
            }
            break;
        }
        if (lines.Count == 0) { return null; }
        var text = string.Join("\n", lines).Replace("\r\n", "\n").Trim('\n');
        return text.Trim().Length == 0 ? null : text;
    }

    /// <summary>Parses a spec comment (the text inside the delimiters, or a <c>.spec.md</c> file).</summary>
    /// <param name="text">The comment text.</param>
    public static SpecComment Parse(string text)
    {
        if (text is null) { throw new ArgumentNullException(nameof(text)); }
        var errors = new List<SpecCommentError>();
        if (text.Length > MaxLength)
        {
            errors.Add(new SpecCommentError(0, "the spec comment is longer than 64 KiB; the rest is not read"));
            text = text.Substring(0, MaxLength);
        }
        text = text.Replace("\r\n", "\n");

        string? title = null;
        AppRole? role = null;
        bool? shipped = null;
        var roleSeen = false;
        var shippedSeen = false;
        var declared = false;
        var operations = new List<string>();
        var scenarios = new List<string>();
        var description = new StringBuilder();
        var inSections = false;

        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            var number = n + 1;
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSections = true;
                var heading = line.Substring(3).Trim();
                if (heading.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase))
                {
                    scenarios.Add(heading.Substring("Scenario:".Length).Trim());
                }
                continue;
            }
            if (inSections) { continue; }
            if (title is null && line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line.Substring(2).Trim();
                continue;
            }
            if (Header(line, "Role", out var roleText))
            {
                if (roleSeen) { errors.Add(new SpecCommentError(number, "Role is declared twice")); continue; }
                roleSeen = true;
                if (ParseRole(roleText, out var parsed)) { role = parsed; }
                else { errors.Add(new SpecCommentError(number, "Role is not one of Shell, Default, Optional, Example")); }
                continue;
            }
            if (Header(line, "Shipped", out var shippedText))
            {
                if (shippedSeen) { errors.Add(new SpecCommentError(number, "Shipped is declared twice")); continue; }
                shippedSeen = true;
                if (string.Equals(shippedText, "true", StringComparison.Ordinal)) { shipped = true; }
                else if (string.Equals(shippedText, "false", StringComparison.Ordinal)) { shipped = false; }
                else { errors.Add(new SpecCommentError(number, "Shipped is not true or false")); }
                continue;
            }
            if (Header(line, "Operations", out var operationsText))
            {
                if (declared) { errors.Add(new SpecCommentError(number, "Operations is declared twice")); continue; }
                declared = true;
                foreach (var part in operationsText.Split(','))
                {
                    var name = NormalizeOperation(part.Trim().Trim('`', '"'));
                    if (name.Length > 0 && !operations.Contains(name)) { operations.Add(name); }
                }
                continue;
            }
            if (description.Length > 0 || line.Length > 0) { description.Append(line).Append('\n'); }
        }

        if (!declared)
        {
            foreach (var name in Invoked(text))
            {
                if (!operations.Contains(name)) { operations.Add(name); }
            }
        }

        return new SpecComment(text, title, description.ToString().Trim(), role, shipped, operations, declared, scenarios, errors);
    }

    private static bool Header(string line, string name, out string value)
    {
        value = string.Empty;
        if (line.Length <= name.Length || !line.StartsWith(name, StringComparison.Ordinal) || line[name.Length] != ':') { return false; }
        value = line.Substring(name.Length + 1).Trim();
        return true;
    }

    private static bool ParseRole(string text, out AppRole role)
    {
        switch (text)
        {
            case "Shell": role = AppRole.Shell; return true;
            case "Default": role = AppRole.Default; return true;
            case "Optional": role = AppRole.Optional; return true;
            case "Example": role = AppRole.Example; return true;
            default: role = default; return false;
        }
    }

    // Names quoted right after an invoke verb: `Invoking "add"`, `posts "clear"`.
    private static IEnumerable<string> Invoked(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var verb in InvokeVerbs)
        {
            var at = 0;
            while ((at = lower.IndexOf(verb, at, StringComparison.Ordinal)) >= 0)
            {
                var start = at + verb.Length;
                at = start;
                if (start - verb.Length > 0 && char.IsLetter(lower[start - verb.Length - 1])) { continue; }
                var i = start;
                while (i < text.Length && text[i] == ' ') { i++; }
                if (i == start || i >= text.Length || (text[i] != '"' && text[i] != '`')) { continue; }
                var close = text.IndexOf(text[i], i + 1);
                if (close < 0) { continue; }
                var name = NormalizeOperation(text.Substring(i + 1, close - i - 1));
                if (name.Length > 0 && IsName(name)) { yield return name; }
            }
        }
    }

    private static IEnumerable<string> Quoted(string text)
    {
        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOfAny(['"', '`'], at);
            if (open < 0) { yield break; }
            var close = text.IndexOf(text[open], open + 1);
            if (close < 0) { yield break; }
            yield return text.Substring(open + 1, close - open - 1);
            at = close + 1;
        }
    }

    private static bool IsName(string name)
    {
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c)) { return false; }
        }
        return true;
    }
}
