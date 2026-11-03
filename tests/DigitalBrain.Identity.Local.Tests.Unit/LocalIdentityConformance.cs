using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class LocalIdentityConformance : IdentityConformance<IIdentity>, IAsyncLifetime
{
    private const string PrivilegedModule = "kit-auth";

    private static readonly ModuleDeclarations Declarations = new([new ModuleManifest([], [], [], [], [])
    {
        Id = PrivilegedModule,
        Privileged =
        [
            new(Capability.Identity.V1, nameof(ISessions.Issue)),
            new(Capability.Identity.V1, nameof(ISessions.End)),
            new(Capability.Identity.V1, nameof(ISessions.Validate)),
        ],
    }]);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "db-dir-" + Guid.NewGuid().ToString("N"));
    private readonly AmbientCallerScope _callers = new();
    private SimpleServiceProvider? _provider;
    private TestOS? _os;
    private PortIdentity? _identity;
    private PortGrants? _grants;
    private PortSessions? _sessions;

    public ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        TestOS? os = null;
        services.AddLocalIdentity((contract, fullKey) => ResolveNeuron(os!, contract, fullKey), _directory);
        _provider = services.BuildServiceProvider();
        _os = os = TestOS.Local(options => options.Services = _provider);
        _os.Register<DirectoryNeuron>();

        var directoryPort = _provider.GetRequiredService<IIdentityDirectoryPort>();
        var sessionPort = _provider.GetRequiredService<IIdentitySessionPort>();
        _identity = new PortIdentity(directoryPort, _callers);
        _grants = new PortGrants(directoryPort, _callers);
        _sessions = new PortSessions(sessionPort, _callers, Declarations);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_os is not null)
        {
            await _os.DisposeAsync();
        }

        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
    }

    protected override Task<IdentitySubject> Connect(CancellationToken ct)
        => Task.FromResult(new IdentitySubject(
            _identity!,
            _grants!,
            _callers,
            async (principalId, token) => (await _identity!.RegisterPrincipal(principalId, principalId, token).ConfigureAwait(false)).Member)
        {
            Sessions = _sessions,
            Description = _sessions,
            PrivilegedModule = PrivilegedModule,
        });

    internal static object ResolveNeuron(TestOS os, Type contract, string fullKey)
    {
        var separator = fullKey.IndexOf('/', StringComparison.Ordinal);
        var brain = fullKey[..separator];
        var key = fullKey[(separator + 1)..];
        var type = contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)!.Type;
        var id = new NeuronId(new BrainId(brain), type, key);
        if (contract == typeof(IDirectory))
        {
            return os.Get<IDirectory>(id);
        }

        throw new InvalidOperationException($"Unsupported neuron contract {contract.FullName}.");
    }

    internal sealed class PortIdentity(IIdentityDirectoryPort port, AmbientCallerScope callers) : IIdentity
    {
        public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default)
            => port.ReadAccount(accountId, cancellationToken);

        public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default)
            => port.OwnerOfBrain(brainId, cancellationToken);

        public Task EnsureAccount(Account account, CancellationToken cancellationToken = default)
            => port.EnsureAccount(account, cancellationToken);

        public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
            => port.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);

        public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default)
            => port.EnsureBrain(owner, cancellationToken);

        public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default)
            => port.ProvisionOwner(owner, cancellationToken);

        public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default)
            => port.IsRegistered(principalId, cancellationToken);

        public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
            => port.RegisterPrincipal(principalId, displayName, cancellationToken);

        public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default)
            => port.CompleteRegistration(principalId, cancellationToken);

        public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default)
            => port.BrainsOf(principalId, cancellationToken);

        public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default)
            => port.KindOf(brainScopeId, cancellationToken);

        public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default)
            => port.BrainExists(brainScopeId, cancellationToken);

        public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
            => port.Membership(brainScopeId, principalId, cancellationToken);

        public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
            => port.EnsureMember(callers.Current, member, cancellationToken);

        public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
            => port.RemoveMember(callers.Current, brainScopeId, accountId, cancellationToken);

        public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
            => port.RemovePrincipalMember(callers.Current, brainScopeId, principalId, cancellationToken);
    }

    internal sealed class PortGrants(IIdentityDirectoryPort port, AmbientCallerScope callers) : IGrants
    {
        public Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return port.Authorize(callers.Current, request, cancellationToken);
        }

        public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default)
            => port.ListGrants(brainScopeId, cancellationToken);

        public async Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(grant);
            var caller = callers.Current;
            return await port.GrantAsOwner(caller, grant, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {grant.WorkspaceId}.");
        }

        public async Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
        {
            var caller = callers.Current;
            if (!await port.RevokeAsOwner(caller, appId, semanticTypeId, mode, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {caller.BrainId}.");
            }
        }
    }

    internal sealed class PortSessions(
        IIdentitySessionPort port,
        AmbientCallerScope callers,
        ModuleDeclarations declarations) : ISessions, IIdentityProvider
    {
        public Task<SessionIssued> Issue(SessionRequest request, CancellationToken cancellationToken = default)
        {
            var caller = Privileged(nameof(Issue));
            SessionClaims.Validate(request);
            return port.Issue(caller, request, cancellationToken);
        }

        public Task End(string sessionId, CancellationToken cancellationToken = default)
        {
            var caller = Privileged(nameof(End));
            ArgumentNullException.ThrowIfNull(sessionId);
            return port.End(caller, sessionId, cancellationToken);
        }

        public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken = default)
        {
            _ = Privileged(nameof(Validate));
            ArgumentNullException.ThrowIfNull(sessionId);
            return port.Validate(sessionId, cancellationToken);
        }

        public IdentityProviderDescriptor Describe() => port.Describe();

        private CapabilityCaller Privileged(string member)
        {
            var caller = callers.Current;
            declarations.RequirePrivileged(caller.AppId, Capability.Identity.V1, member);
            return caller;
        }
    }
}
