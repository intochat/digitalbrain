using DigitalBrain.Client.Orleans;
using DigitalBrain.Client;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Testing.E2E;

public sealed class AspireTestSession : IAsyncDisposable
{
    private readonly TestSessionLifetime _lifetime;
    private readonly TestExecutionOptions _options;
    private IHost? _client;
    private readonly DigitalBrainBuilder _brain;
    private readonly string _serverName;
    private AspireTestSession(DistributedApplication app, TestSessionLifetime lifetime, TestExecutionOptions options, DigitalBrainBuilder brain, string serverName)
    { App = app; _lifetime = lifetime; _options = options; _brain = brain; _serverName = serverName; }

    public DistributedApplication App { get; }
    public IDigitalBrain Brain => _client!.Services.GetRequiredService<IDigitalBrain>();
    public HttpClient HttpClient { get; private set; } = null!;
    public Uri? BrowserEndpoint { get; private set; }
    public string? BrowserReadySelector { get; private set; }
    public TestExecutionOptions Options => _options;
    public TestSessionLifetime Lifetime => _lifetime;

    public static Task<AspireTestSession> StartAsync<TAppHost>(
        IReadOnlyList<string> args, TestExecutionOptions options, CancellationToken cancellationToken)
        where TAppHost : class
        => StartCoreAsync(ct => DistributedApplicationTestingBuilder.CreateAsync<TAppHost>([.. args], ct),
            declareTopology: null, options, cancellationToken);

    public static Task<AspireTestSession> StartAsync(TestExecutionOptions options,
        Action<IDistributedApplicationTestingBuilder> declareTopology, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declareTopology);
        return StartCoreAsync(_ => Task.FromResult(CreateModuleBuilder()),
            declareTopology, options, cancellationToken);
    }

    private static IDistributedApplicationTestingBuilder CreateModuleBuilder()
    {
        // Aspire discovers its metadata on the caller stack. The build-transitive factory is
        // compiled into the consuming assembly, even when an assembly fixture starts the host.
        var factory = System.Reflection.Assembly.GetEntryAssembly()!
            .GetType("DigitalBrain.Testing.Generated.ModuleBuilderFactory", throwOnError: true)!;
        return (IDistributedApplicationTestingBuilder)factory.GetMethod("Create")!.Invoke(null, null)!;
    }

    private static async Task<AspireTestSession> StartCoreAsync(
        Func<CancellationToken, Task<IDistributedApplicationTestingBuilder>> createBuilder,
        Action<IDistributedApplicationTestingBuilder>? declareTopology,
        TestExecutionOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.StartupTimeout);
        var ct = deadline.Token;
        var lifetime = new TestSessionLifetime(options);
        var stage = "builder";
        try
        {
            // Module hosts explicitly use the consuming test assembly's Aspire metadata.
            var builder = await createBuilder(ct).ConfigureAwait(false);
            lifetime.Own("builder", builder);
            PrivateTestConfiguration? privateSettings = null;
            if (options.PrivateConfiguration.Count > 0)
            {
                privateSettings = await PrivateTestConfiguration.CreateAsync(options.PrivateConfiguration, ct).ConfigureAwait(false);
                lifetime.Own("private-configuration", privateSettings);
            }
            // Provider parameters are evaluated when Aspire starts resources. Keep their
            // values out of command-line arguments and public composition envelopes.
            builder.Configuration.AddInMemoryCollection(options.PrivateConfiguration
                .Where(pair => pair.Key.StartsWith("Parameters:", StringComparison.OrdinalIgnoreCase)));
            // Aspire forwards every resource's console output under "<AppHost>.Resources.<resource>"; config rules beat SetMinimumLevel.
            builder.Configuration.AddInMemoryCollection(TestLogging.QuietDefaults);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Logging:LogLevel:{builder.Environment.ApplicationName}.Resources"] = "Warning",
            });
            builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            stage = "topology";
            declareTopology?.Invoke(builder);
            var hosts = SiloHosts.Find(builder.Resources);
            var selectedHost = SiloHosts.Select(builder.Resources, options.ServerResourceName);
            var selectedBrain = SiloHosts.BrainOf(selectedHost);
            if (hosts.Count == 0)
            { throw new InvalidOperationException("The AppHost must reference the brain from at least one resource with an http endpoint."); }
            foreach (var resource in builder.Resources.Where(r => r is ProjectResource or ExecutableResource))
            {
                resource.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                {
                    foreach (var (key, value) in TestLogging.QuietDefaults)
                    { context.EnvironmentVariables[key.Replace(":", "__")] = value!; }
                }));
            }
            foreach (var resource in hosts)
            {
                if (privateSettings is not null)
                {
                    resource.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                        context.EnvironmentVariables[DigitalBrainNames.ConfigurationFileEnvironmentVariable] = privateSettings.FilePath));
                }
                if (options.ResourceEnvironment.Count > 0)
                {
                    resource.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                    {
                        foreach (var (key, value) in options.ResourceEnvironment) { context.EnvironmentVariables[key] = value; }
                    }));
                }
                // The AppHost publishes a fixed port. A testing builder also copies launchSettings ports,
                // so clear both or every session binds the same endpoint.
                foreach (var http in resource.Annotations.OfType<EndpointAnnotation>())
                {
                    if (!string.Equals(http.Name, SiloHosts.HttpEndpointName, StringComparison.Ordinal)) { continue; }
                    http.Port = null;
                    http.TargetPort = null;
                }
            }
            stage = "build";
            var app = await builder.BuildAsync(ct).ConfigureAwait(false);
            lifetime.Own("application", app);
            var session = new AspireTestSession(app, lifetime, options, selectedBrain, selectedHost.Name);
            stage = "start";
            await app.StartAsync(ct).WaitAsync(ct).ConfigureAwait(false);
            stage = "readiness";
            foreach (var host in hosts)
            { await app.ResourceNotifications.WaitForResourceHealthyAsync(host.Name, ct).ConfigureAwait(false); }
            var browser = builder.Resources.SelectMany(r => r.Annotations.OfType<BrainBrowserAnnotation>()
                .Select(a => (r.Name, a.Endpoint, a.Path, a.ReadySelector))).ToArray();
            if (browser.Length > 1) { throw new InvalidOperationException("The AppHost declares multiple primary browser endpoints."); }
            if (browser.Length == 1)
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(browser[0].Name, ct).ConfigureAwait(false);
                if (!browser[0].Path.StartsWith('/') || browser[0].Path.StartsWith("//", StringComparison.Ordinal)
                    || browser[0].Path.Contains('\\'))
                { throw new InvalidOperationException("Browser navigation must be relative to the advertised endpoint."); }
                session.BrowserEndpoint = new Uri(app.GetEndpoint(browser[0].Name, browser[0].Endpoint), browser[0].Path);
                session.BrowserReadySelector = browser[0].ReadySelector;
            }
            stage = "client";
            await session.ConnectAsync(ct).ConfigureAwait(false);
            lifetime.Own("client", new AsyncAction(session.ReleaseClientAsync));
            session.HttpClient = app.CreateHttpClient(selectedHost.Name, SiloHosts.HttpEndpointName);
            lifetime.Own("http", new AsyncAction(() => { session.HttpClient.Dispose(); return ValueTask.CompletedTask; }));
            if (session.BrowserEndpoint is not null
                && options.ResourceEnvironment.ContainsKey("DigitalBrain__Testing__ReferenceComposition"))
            {
                stage = "reference-browser-bundle";
                // Sequential suites reuse Flutter's build directory. A cached bundle can answer
                // health probes while still targeting the previous suite's now-stopped runtime.
                using var probe = new HttpClient();
                var runtimeEndpoint = session.HttpClient.BaseAddress!.GetLeftPart(UriPartial.Authority);
                while (true)
                {
                    using var response = await probe.GetAsync(new Uri(session.BrowserEndpoint, "/main.dart.js"), ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode
                        && (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Contains(runtimeEndpoint, StringComparison.Ordinal))
                    { break; }
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
            }
            return session;
        }
        catch (Exception error)
        {
            try { options.Diagnostics?.Invoke(new(stage, "Startup failed; releasing owned resources.")); } catch { }
            try { await lifetime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException($"Startup failed at '{stage}' and rollback failed.", error, cleanup); }
            throw new InvalidOperationException($"Test startup failed at '{stage}'.", error);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var connection = _brain.ClusteringResourceName is { } clustering
            ? await App.GetConnectionStringAsync(clustering, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Missing brain clustering connection.")
            : null;
        var host = Host.CreateDefaultBuilder().ConfigureLogging(log => log.SetMinimumLevel(LogLevel.Warning))
            .UseOrleansClient(client =>
            {
                if (connection is not null)
                { client.UseAzureStorageClustering(storage => storage.TableServiceClient = new Azure.Data.Tables.TableServiceClient(connection)); }
                else
                {
                    var endpoint = App.GetEndpoint(_serverName, "orleans-gateway");
                    client.UseStaticClustering(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, endpoint.Port));
                }
                client.Configure<ClusterOptions>(cluster => { cluster.ClusterId = _brain.ClusterId; cluster.ServiceId = _brain.ServiceId; });
                client.AddDigitalBrain();
            }).Build();
        try { await host.StartAsync(ct).WaitAsync(ct).ConfigureAwait(false); _client = host; }
        catch { host.Dispose(); throw; }
    }

    private async ValueTask ReleaseClientAsync()
    {
        var host = Interlocked.Exchange(ref _client, null);
        if (host is null) { return; }
        using var deadline = new CancellationTokenSource(_options.CleanupTimeout);
        try
        {
            await host.Services.GetRequiredService<IDigitalBrain>().DisposeAsync().AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            await host.StopAsync(deadline.Token).ConfigureAwait(false);
        }
        finally { host.Dispose(); }
    }

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();
    private sealed class AsyncAction(Func<ValueTask> action) : IAsyncDisposable
    { public ValueTask DisposeAsync() => action(); }
}
