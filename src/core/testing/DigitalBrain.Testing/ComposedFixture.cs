using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DigitalBrain.Testing;

internal sealed class ComposedFixture : DigitalBrainFixture
{
    private readonly Action<DigitalBrainTestBuilder> _compose;
    private readonly Lazy<Task> _boot;

    internal ComposedFixture(Action<DigitalBrainTestBuilder> compose)
    {
        _compose = compose;
        Fingerprint = FingerprintOf(compose);
        _boot = new(
            () => InitializeAsync().AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal string Fingerprint { get; }

    internal bool HasBooted => _boot.IsValueCreated;

    internal static string FingerprintOf(Action<DigitalBrainTestBuilder> compose)
    {
        var builder = new DigitalBrainTestBuilder();
        compose(builder);
        var composition = builder.Seal();

        var modules = composition.Modules
            .Select(module => module.Id.Value)
            .Order(StringComparer.Ordinal);
        var configuration = composition.Configuration
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value}");
        var timeout = composition.ResponseTimeout?.ToString("c", CultureInfo.InvariantCulture) ?? "-";

        return string.Join('|', [.. modules, .. configuration, timeout]);
    }

    internal async Task<TestBrain> LeaseAsync(CancellationToken cancellationToken)
    {
        await _boot.Value.ConfigureAwait(false);
        return await CreateBrainAsync(cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failed boot is reported to the leasing test; teardown only needs it settled.")]
    internal async Task SettleAsync()
    {
        if (!_boot.IsValueCreated)
        {
            return;
        }

        try
        {
            await _boot.Value.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    protected override void Configure(DigitalBrainTestBuilder brain)
        => _compose(brain);
}
