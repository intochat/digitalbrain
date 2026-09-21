using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
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
    private readonly string _identity;
    private AspireTestSession(DistributedApplication app, TestSessionLifetime lifetime, TestExecutionOptions options, string identity)
    { App = app; _lifetime = lifetime; _options = options; _identity = identity; }

    public DistributedApplication App { get; }
    public IDigitalBrain Brain => _client!.Services.GetRequiredService<IDigitalBrain>();
    public HttpClient HttpClient { get; private set; } = null!;
    public Uri? BrowserEndpoint { get; private set; }
    public string? BrowserReadySelector { get; private set; }
    public TestExecutionOptions Options => _options;
    public TestSessionLifetime Lifetime => _lifetime;

    public static Task<AspireTestSession> StartAsync<TAppHost>(
        IReadOnlyList<string> args, string identity, TestExecutionOptions options, CancellationToken cancellationToken)
        where TAppHost : class
        => StartCoreAsync(ct => DistributedApplicationTestingBuilder.CreateAsync<TAppHost>([.. args], ct),
            declareTopology: null, identity, options, cancellationToken);

    public static Task<AspireTestSession> StartAsync(string identity, TestExecutionOptions options,
        Action<IDistributedApplicationTestingBuilder> declareTopology, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declareTopology);
        return StartCoreAsync(_ => Task.FromResult(DistributedApplicationTestingBuilder.Create([])),
            declareTopology, identity, options, cancellationToken);
    }

    private static async Task<AspireTestSession> StartCoreAsync(
        Func<CancellationToken, Task<IDistributedApplicationTestingBuilder>> createBuilder,
        Action<IDistributedApplicationTestingBuilder>? declareTopology,
        string identity, TestExecutionOptions options, CancellationToken cancellationToken)
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
            PrivateTestConfiguration? privateSettings = null;
            if (options.PrivateConfiguration.Count > 0)
            {
                privateSettings = await PrivateTestConfiguration.CreateAsync(options.PrivateConfiguration, ct).ConfigureAwait(false);
                lifetime.Own("private-configuration", privateSettings);
            }
            var builder = await createBuilder(ct).ConfigureAwait(false);
            lifetime.Own("builder", builder);
            // Provider parameters are evaluated when Aspire starts resources. Keep their
            // values out of command-line arguments and public composition envelopes.
            builder.Configuration.AddInMemoryCollection(options.PrivateConfiguration
                .Where(pair => pair.Key.StartsWith("Parameters:", StringComparison.OrdinalIgnoreCase)));
            builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            stage = "topology";
            declareTopology?.Invoke(builder);
            var primary = builder.Resources.Where(r => r.Annotations.OfType<BrainEndpointAnnotation>().Any()).ToArray();
            if (primary.Length != 1) { throw new InvalidOperationException("The AppHost must declare exactly one primary brain HTTP endpoint."); }
            var resource = primary[0];
            if (privateSettings is not null)
            {
                resource.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                    context.EnvironmentVariables["DigitalBrain__Testing__PrivateConfiguration"] = privateSettings.FilePath));
            }
            var endpoint = resource.Annotations.OfType<BrainEndpointAnnotation>().Single();
            stage = "build";
            var app = await builder.BuildAsync(ct).ConfigureAwait(false);
            lifetime.Own("application", app);
            var session = new AspireTestSession(app, lifetime, options, identity);
            stage = "start";
            await app.StartAsync(ct).ConfigureAwait(false);
            stage = "readiness";
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resource.Name, ct).ConfigureAwait(false);
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
            session.HttpClient = app.CreateHttpClient(resource.Name, endpoint.Endpoint);
            lifetime.Own("http", new AsyncAction(() => { session.HttpClient.Dispose(); return ValueTask.CompletedTask; }));
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
        var connection = await App.GetConnectionStringAsync(DigitalBrainNames.Clustering, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Missing brain clustering connection.");
        var host = Host.CreateDefaultBuilder().ConfigureLogging(log => log.SetMinimumLevel(LogLevel.Warning))
            .UseOrleansClient(client =>
            {
                client.UseAzureStorageClustering(storage => storage.TableServiceClient = new Azure.Data.Tables.TableServiceClient(connection));
                client.Configure<ClusterOptions>(cluster => { cluster.ClusterId = _identity; cluster.ServiceId = _identity; });
                client.AddDigitalBrain();
            }).Build();
        try { await host.StartAsync(ct).ConfigureAwait(false); _client = host; }
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