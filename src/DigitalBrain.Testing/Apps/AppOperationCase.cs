using System.Collections.Concurrent;
using Xunit.Sdk;

namespace DigitalBrain.Testing;

/// <summary>
/// One row of an app's baseline theory: an operation of one app, how to install the app and which fakes to bind.
/// Made by <see cref="AppBaseline.Cases(Type)"/>, run by <see cref="AppBaseline.RunAsync(AppOperationCase)"/>.
/// Its display name is <c>app/operation</c>.
/// </summary>
public sealed class AppOperationCase : IXunitSerializable
{
    // Rows are rebuilt from a token in the same test process: the install delegate and the fakes are not serializable.
    private static readonly ConcurrentDictionary<string, AppOperationCase> Known = new(StringComparer.Ordinal);
    private string _token = string.Empty;

    /// <summary>For the xunit serializer only.</summary>
    [Obsolete("For the xunit serializer only; use AppBaseline.Cases.")]
    public AppOperationCase()
    {
        Manifest = new AppManifest("unknown", "unknown", "0.0.0", [], new AppRequirements([]));
        Operation = string.Empty;
        Install = static _ => throw new InvalidOperationException("This case was not built by AppBaseline.Cases.");
    }

    internal AppOperationCase(AppManifest manifest, string operation, Func<TestOS, Task<IInstalledApp>> install, Action<TestOS>? fakes, bool opens)
    {
        Manifest = manifest;
        Operation = operation;
        Install = install;
        Fakes = fakes;
        OpensFirst = opens;
        _token = $"{manifest.Id}/{operation}/{Guid.NewGuid():N}";
        Known[_token] = this;
    }

    /// <summary>The manifest of the app under test.</summary>
    public AppManifest Manifest { get; private set; }

    /// <summary>The operation this row requests, normalised (<c>open</c>).</summary>
    public string Operation { get; private set; }

    /// <summary><c>app/operation</c>.</summary>
    public string DisplayName => $"{Manifest.Id}/{Operation}";

    internal Func<TestOS, Task<IInstalledApp>> Install { get; private set; }

    internal Action<TestOS>? Fakes { get; private set; }

    // The app has an "open" operation and this row is another one: the app is opened first, as a person would.
    internal bool OpensFirst { get; private set; }

    /// <inheritdoc />
    public override string ToString() => DisplayName;

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _token = info.GetValue<string>("token") ?? string.Empty;
        if (!Known.TryGetValue(_token, out var known))
        {
            throw new InvalidOperationException($"Baseline case '{_token}' is unknown in this process; build the theory data with AppBaseline.Cases.");
        }
        Manifest = known.Manifest;
        Operation = known.Operation;
        Install = known.Install;
        Fakes = known.Fakes;
        OpensFirst = known.OpensFirst;
    }

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue("token", _token);
    }
}

/// <summary>An app failed its baseline: the message names the app, the operation and what went wrong.</summary>
/// <param name="message">What failed.</param>
/// <param name="inner">The error the app raised, if any.</param>
public sealed class AppBaselineException(string message, Exception? inner = null) : Exception(message, inner);
