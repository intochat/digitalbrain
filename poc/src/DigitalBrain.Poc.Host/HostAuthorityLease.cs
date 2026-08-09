using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

internal sealed class HostAuthorityLease : IAsyncDisposable
{
    internal const string ControlTokenEnvironment = "DIGITALBRAIN_POC_HOST_AUTHORITY_CONTROL";
    private const string ActiveLockFileName = "host-authority.lock";
    private const string TransitionLockFileName = "host-transition.lock";

    private readonly string? _activeLeasePath;
    private readonly string? _authorityControlToken;
    private FileStream? _activeLease;
    private FileStream? _standaloneTransitionLease;
    private FileStream? _transitionLease;

    private HostAuthorityLease(FileStream transitionLease)
    {
        _transitionLease = transitionLease;
        _authorityControlToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private HostAuthorityLease(
        FileStream activeLease,
        string activeLeasePath,
        string? authorityControlToken,
        FileStream? standaloneTransitionLease = null)
    {
        _activeLease = activeLease;
        _activeLeasePath = activeLeasePath;
        _authorityControlToken = authorityControlToken;
        _standaloneTransitionLease = standaloneTransitionLease;
    }

    public static Task<HostAuthorityLease?> TryAcquireAsync(
        PocDataRoot root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        var transitionPath = LockPath(root, TransitionLockFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(transitionPath)!);
        FileStream? transitionLease = null;
        try
        {
            transitionLease = OpenExclusive(transitionPath);
            if (!IsActiveAuthorityAvailable(root))
            {
                transitionLease.Dispose();
                return Task.FromResult<HostAuthorityLease?>(null);
            }

            return Task.FromResult<HostAuthorityLease?>(new HostAuthorityLease(transitionLease));
        }
        catch (IOException)
        {
            transitionLease?.Dispose();
            return Task.FromResult<HostAuthorityLease?>(null);
        }
    }

    public static Task<HostAuthorityLease> AcquireForActiveHostAsync(
        PocDataRoot root,
        bool delegatedBySignedSupervisor,
        string? authorityControlToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        var activePath = LockPath(root, ActiveLockFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        FileStream activeLease;
        try
        {
            activeLease = OpenExclusive(activePath);
        }
        catch (IOException)
        {
            throw new HostQuiescingException();
        }

        if (!delegatedBySignedSupervisor)
        {
            try
            {
                var transitionLease = OpenExclusive(LockPath(root, TransitionLockFileName));
                return Task.FromResult(new HostAuthorityLease(
                    activeLease,
                    activePath,
                    null,
                    transitionLease));
            }
            catch (IOException)
            {
                activeLease.Dispose();
                throw new HostQuiescingException();
            }
        }

        if (delegatedBySignedSupervisor && string.IsNullOrWhiteSpace(authorityControlToken))
        {
            activeLease.Dispose();
            throw new AuthorizationException("A signed supervisor delegation has no handoff capability.");
        }

        return Task.FromResult(new HostAuthorityLease(activeLease, activePath, authorityControlToken));
    }

    public void AddChildControlToken(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (_authorityControlToken is null || _transitionLease is null)
        {
            throw new InvalidOperationException("Only the transition-lock owner can delegate host authority.");
        }

        environment[ControlTokenEnvironment] = _authorityControlToken;
    }

    public string ControlToken => _authorityControlToken ?? throw new InvalidOperationException(
        "Only the transition-lock owner has a host-authority control capability.");

    public bool AuthorizesControlToken(string? token) =>
        _activeLeasePath is not null &&
        _authorityControlToken is not null &&
        token is not null &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_authorityControlToken),
            Encoding.UTF8.GetBytes(token));

    public ValueTask ReleaseActiveAuthorityAsync()
    {
        if (_activeLeasePath is null)
        {
            throw new InvalidOperationException("The transition lease cannot release active host authority.");
        }

        Interlocked.Exchange(ref _activeLease, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task ReacquireActiveAuthorityAsync(CancellationToken cancellationToken)
    {
        if (_activeLeasePath is null)
        {
            throw new InvalidOperationException("The transition lease cannot acquire active host authority.");
        }

        if (Volatile.Read(ref _activeLease) is not null)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var acquired = OpenExclusive(_activeLeasePath);
                if (Interlocked.CompareExchange(ref _activeLease, acquired, null) is null)
                {
                    return;
                }

                acquired.Dispose();
                return;
            }
            catch (IOException)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _activeLease, null)?.Dispose();
        Interlocked.Exchange(ref _standaloneTransitionLease, null)?.Dispose();
        Interlocked.Exchange(ref _transitionLease, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool IsActiveAuthorityAvailable(PocDataRoot root) =>
        IsLockAvailable(LockPath(root, ActiveLockFileName));

    private static bool IsLockAvailable(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var probe = OpenExclusive(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static FileStream OpenExclusive(string path) =>
        new(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.WriteThrough);

    private static string LockPath(PocDataRoot root, string fileName) =>
        Path.Combine(root.RootPath, fileName);
}
