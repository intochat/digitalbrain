using System.IO.Pipes;
using System.Text.Json;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using System.Reflection;

namespace DigitalBrain.Core;

public static class BehaviorApp
{
    public static async Task RunAsync<TBehavior>(string[] args,
        Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>> requirements) where TBehavior : class, IBehavior
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.UseOrleansClient(client =>
        {
            client.AddDigitalBrain();
            // Raw assembly references in generated apps do not emit Orleans ApplicationPart attributes.
            client.Services.AddSerializer(serializer =>
            {
                serializer.AddAssembly(typeof(TBehavior).Assembly);
                foreach (var reference in typeof(TBehavior).Assembly.GetReferencedAssemblies())
                { serializer.AddAssembly(Assembly.Load(reference)); }
            });
            var gateways = builder.Configuration["Gateways"];
            if (string.IsNullOrWhiteSpace(gateways))
            {
                if (!builder.Configuration.GetValue<bool>("LocalDevelopment"))
                { throw new InvalidOperationException("Supply explicit Gateways, ClusterId and ServiceId, or enable LocalDevelopment."); }
                client.UseLocalhostClustering();
            }
            else
            {
                client.UseStaticClustering(options => options.Gateways = gateways.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => new Uri(x)).ToList());
                client.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = builder.Configuration["ClusterId"] ?? throw new InvalidOperationException("ClusterId is required.");
                    options.ServiceId = builder.Configuration["ServiceId"] ?? throw new InvalidOperationException("ServiceId is required.");
                });
            }
        });
        using var host = builder.Build();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        var readiness = new BehaviorReadiness();
        readiness.End(readiness.Begin(typeof(TBehavior).FullName!, []));
        var pipeName = Environment.GetEnvironmentVariable("BRAIN_CONTROL_PIPE");
        using var pipe = pipeName is null ? null : new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task control = Task.CompletedTask;
        Task report = Task.CompletedTask;
        try
        {
            if (pipe is not null)
            {
                await pipe.ConnectAsync(30000, stopping.Token).ConfigureAwait(false);
                var generation = Guid.Parse(Environment.GetEnvironmentVariable("BRAIN_GENERATION") ?? throw new InvalidOperationException("Missing generation."));
                var token = Environment.GetEnvironmentVariable("BRAIN_CONTROL_TOKEN") ?? throw new InvalidOperationException("Missing control token.");
                control = ReceiveControl(pipe, generation, stopping);
                report = Report(pipe, generation, token, readiness, stopping.Token);
            }
            await host.StartAsync(stopping.Token).ConfigureAwait(false);
            var brain = host.Services.GetRequiredService<IDigitalBrain>();
            var generationId = readiness.Begin(typeof(TBehavior).FullName!, requirements(brain));
            await using var scoped = new BehaviorScopedBrain(brain, readiness, generationId);
            var behavior = ActivatorUtilities.CreateInstance<TBehavior>(new BehaviorServices(host.Services, scoped));
            var run = behavior.RunAsync(stopping.Token);
            var loss = readiness.WaitForLossAsync(generationId);
            if (await Task.WhenAny(run, loss).ConfigureAwait(false) == loss)
            {
                await stopping.CancelAsync().ConfigureAwait(false);
                try { await run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                throw new InvalidOperationException("A required subscription closed; live signals may have been missed.");
            }
            await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(control, report).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or IOException or TimeoutException) { }
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.StopAsync(shutdown.Token).ConfigureAwait(false);
        }
    }

    private sealed class BehaviorServices(IServiceProvider services, IDigitalBrain brain) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IDigitalBrain) ? brain : services.GetService(serviceType);
    }

    private static async Task ReceiveControl(Stream pipe, Guid generation, CancellationTokenSource stopping)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        long sequence = 0;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                deadline.CancelAfter(ControlDuration("BRAIN_HEARTBEAT_LOSS_MS", TimeSpan.FromSeconds(20)));
                var line = await ReadControlLineAsync(reader, deadline.Token).ConfigureAwait(false);
                var message = JsonSerializer.Deserialize<BehaviorControlMessage>(line);
                if (message is null || message.Version != 1 || message.GenerationId != generation || message.Sequence <= sequence)
                { throw new IOException("Invalid behavior control message."); }
                sequence = message.Sequence;
                if (message.Kind == "stop") { break; }
                if (message.Kind != "heartbeat") { throw new IOException("Unknown behavior control message."); }
            }
        }
        finally { await stopping.CancelAsync().ConfigureAwait(false); }
    }

    private static async Task Report(Stream pipe, Guid generation, string token, BehaviorReadiness readiness, CancellationToken ct)
    {
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        long sequence = 1;
        await writer.WriteLineAsync(JsonSerializer.Serialize(new BehaviorControlMessage(1, generation, sequence, "hello", Token: token)).AsMemory(), ct).ConfigureAwait(false);
        while (true)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new BehaviorControlMessage(1, generation, ++sequence, "heartbeat", readiness.IsReady)).AsMemory(), ct).ConfigureAwait(false);
            await Task.Delay(ControlDuration("BRAIN_HEARTBEAT_MS", TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
        }
    }

    private static TimeSpan ControlDuration(string key, TimeSpan fallback)
        => double.TryParse(Environment.GetEnvironmentVariable(key), System.Globalization.CultureInfo.InvariantCulture, out var milliseconds)
            && double.IsFinite(milliseconds) && milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : fallback;

    public static async Task<string> ReadControlLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new System.Text.StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character, cancellationToken).ConfigureAwait(false) == 1)
        {
            if (character[0] == '\n') { return text.ToString(); }
            if (text.Length >= 4096) { throw new IOException("Behavior control message exceeds its limit."); }
            text.Append(character[0]);
        }
        throw new EndOfStreamException("Behavior control channel closed.");
    }
}
