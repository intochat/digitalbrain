using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

// Stamps the kit caller the way a host edge does: through RuntimeCallerContext.
internal sealed class RuntimeCallerScope : CallerScope
{
    public override IDisposable As(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return RuntimeCallerContext.Stamp(new CapabilityCaller(caller.PrincipalId, caller.AccountId, caller.BrainId, caller.AppId, caller.ConversationId));
    }
}

// Captures every formatted log line at every level, so LogsNeverContainValue has teeth.
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Logger(_lines);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue($"{logLevel} {eventId.Name} {formatter(state, exception)} {exception}");
    }
}

// The secrets@1 kit against the runtime capability over the in-memory vault port.
public sealed class SecretsCapabilityConformance : SecretsConformance<SecretsCapability>
{
    protected override Task<SecretsSubject> Connect(CancellationToken ct)
    {
        var logs = new CapturingLoggerProvider();
        var vault = new InMemorySecretVault();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var secrets = new SecretsCapability(vault, factory.CreateLogger<SecretsCapability>()) { Declarations = KitDeclarations.Identity };
        vault.Provide(KitDeclarations.NamedSecret.Name, KitDeclarations.NamedSecret.Value);
        return Task.FromResult(new SecretsSubject(secrets, new RuntimeCallerScope(), () => [.. logs.Lines, .. vault.Log]) { Named = KitDeclarations.NamedSecret });
    }
}

// The identity@1 kit against the runtime capabilities over the in-memory directory port.
public sealed class IdentityCapabilityConformance : IdentityConformance<IdentityCapability>
{
    protected override Task<IdentitySubject> Connect(CancellationToken ct)
    {
        var directory = new InMemoryIdentityDirectory();
        var sessions = new SessionsCapability(directory, KitDeclarations.Identity);
        return Task.FromResult(new IdentitySubject(new IdentityCapability(directory), new GrantsCapability(directory), new RuntimeCallerScope(), directory.SignIn)
        {
            Sessions = sessions,
            Description = sessions,
            PrivilegedModule = KitDeclarations.PrivilegedModule,
        });
    }
}

// The manifest declarations the 09-03 kit facts run under: one module granted the identity@1 session members and
// declaring the kit-issuer secret.
internal static class KitDeclarations
{
    public const string PrivilegedModule = "kit-auth";

    public static KitNamedSecret NamedSecret { get; } = new(PrivilegedModule, "kit-issuer:SigningKey", $"v-{Guid.NewGuid():N}");

    public static ModuleDeclarations Identity { get; } = new([new ModuleManifest([], [], [], [], [Secret.Named("kit-issuer")])
    {
        Id = PrivilegedModule,
        Privileged = [new(Capability.Identity.V1, "Issue"), new(Capability.Identity.V1, "End"), new(Capability.Identity.V1, "Validate")],
    }]);
}
