using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

// TOPO-01: command line > environment variables > <Home>/settings.json > topology defaults > module (options class) defaults.
public sealed class ConfigurationPrecedenceFacts : IDisposable
{
    private const string Key = "Sample:Key";
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-prec-" + Guid.NewGuid().ToString("N"));
    private readonly string _prefix = "DBPREC" + Guid.NewGuid().ToString("N")[..8] + "_";

    public ConfigurationPrecedenceFacts() => Directory.CreateDirectory(_home);

    public sealed class SampleOptions
    {
        public string Key { get; set; } = "module";
        public string Other { get; set; } = "module";
    }

    private ConfigurationManager Resolve(bool settingsFile, bool environment, bool commandLine)
    {
        var settings = Path.Combine(_home, "settings.json");
        if (settingsFile) { File.WriteAllText(settings, """{ "Sample": { "Key": "settings" } }"""); }
        else if (File.Exists(settings)) { File.Delete(settings); }
        Environment.SetEnvironmentVariable(_prefix + "Sample__Key", environment ? "env" : null);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new(IntoChatOSNames.HomeKey, _home)]);
        builder.Configuration.AddEnvironmentVariables(_prefix);
        builder.Configuration.AddCommandLine(commandLine ? ["--Sample:Key=cli"] : []);

        builder.UseTopology([], [], new FakeTopology(Topology.Local, defaults: new Dictionary<string, string?> { [Key] = "default" }));
        return builder.Configuration;
    }

    [Fact]
    public void Each_source_wins_over_the_ones_below_it()
    {
        Assert.Equal("cli", Resolve(settingsFile: true, environment: true, commandLine: true)[Key]);
        Assert.Equal("env", Resolve(settingsFile: true, environment: true, commandLine: false)[Key]);
        Assert.Equal("settings", Resolve(settingsFile: true, environment: false, commandLine: false)[Key]);
        Assert.Equal("default", Resolve(settingsFile: false, environment: false, commandLine: false)[Key]);
    }

    [Fact]
    public void Options_class_default_applies_only_when_no_source_sets_the_key()
    {
        var options = new SampleOptions();
        Resolve(settingsFile: false, environment: false, commandLine: false).GetSection("Sample").Bind(options);

        Assert.Equal("default", options.Key);
        Assert.Equal("module", options.Other);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_prefix + "Sample__Key", null);
        if (Directory.Exists(_home)) { Directory.Delete(_home, recursive: true); }
    }
}
