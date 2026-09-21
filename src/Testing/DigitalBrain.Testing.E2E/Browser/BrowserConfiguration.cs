namespace DigitalBrain.Testing.E2E;

public sealed class BrowserConfiguration
{
    private BrowserOptions _options;
    private BrowserConfiguration(BrowserOptions options) => _options = options;
    public BrowserConfiguration Headed() { _options = _options with { Headless = false }; return this; }
    public BrowserConfiguration Headless() { _options = _options with { Headless = true }; return this; }
    public BrowserConfiguration SlowMo(float milliseconds)
    {
        if (!float.IsFinite(milliseconds) || milliseconds < 0) { throw new ArgumentOutOfRangeException(nameof(milliseconds)); }
        _options = _options with { SlowMoMilliseconds = milliseconds };
        return this;
    }

    private static readonly AsyncLocal<Scope?> Current = new();
    public static void Configure(Action<BrowserConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var scope = Current.Value ?? throw new InvalidOperationException("Browser configuration requires an E2E builder's module callback.");
        var browser = new BrowserConfiguration(scope.Options);
        configure(browser);
        scope.Options = browser._options;
    }

    internal static Scope Begin(BrowserOptions options) => new(options);
    internal sealed class Scope : IDisposable
    {
        private readonly Scope? _previous;
        internal BrowserOptions Options { get; set; }
        internal Scope(BrowserOptions options) { Options = options; _previous = Current.Value; Current.Value = this; }
        public void Dispose() => Current.Value = _previous;
    }
}
