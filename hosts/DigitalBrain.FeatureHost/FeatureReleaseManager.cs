using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.FeatureHost;

public sealed record FeatureReleaseDescriptor(ReleaseDigest Digest, string ReleaseDirectory);

public sealed record FeatureActiveInstallation(
    FeatureInstallationId InstallationId,
    FeatureReleaseDescriptor Release);

public interface IFeatureHostRecycle
{
    void RequestRecycle();
}

public sealed class FeatureReleaseValidationException : Exception
{
    public FeatureReleaseValidationException(string message)
        : base(message)
    {
    }

    public FeatureReleaseValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class FeatureReleaseUnavailableException : Exception
{
    public FeatureReleaseUnavailableException(FeatureInstallationId installationId, ReleaseDigest release)
        : base($"Feature release '{release}' is not active for installation '{installationId}'.")
    {
    }
}

public sealed class FeatureReleaseLease : IDisposable, IAsyncDisposable
{
    private ReleaseSlot? _slot;
    private IFeature? _feature;

    internal FeatureReleaseLease(ReleaseSlot slot, IFeature feature)
    {
        _slot = slot;
        _feature = feature;
        Digest = slot.Digest;
    }

    public ReleaseDigest Digest { get; }
    public IFeature Feature => _feature ?? throw new ObjectDisposedException(nameof(FeatureReleaseLease));

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        var slot = Interlocked.Exchange(ref _slot, null);
        if (slot is null)
            return;
        var feature = Interlocked.Exchange(ref _feature, null)!;
        try
        {
            if (feature is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (feature is IDisposable disposable)
                disposable.Dispose();
        }
        finally
        {
            slot.Release();
        }
    }

    internal async Task<bool> TryDisposeAsync(TimeSpan deadline)
    {
        var slot = Interlocked.Exchange(ref _slot, null);
        if (slot is null)
            return true;
        var feature = Interlocked.Exchange(ref _feature, null)!;
        try
        {
            var disposal = Task.Run(() => DisposeFeatureAsync(feature));
            await disposal.WaitAsync(deadline);
            slot.Release();
            return true;
        }
        catch
        {
            slot.AbandonLease();
            return false;
        }
    }

    internal void Abandon()
    {
        var slot = Interlocked.Exchange(ref _slot, null);
        Interlocked.Exchange(ref _feature, null);
        slot?.AbandonLease();
    }

    private static async Task DisposeFeatureAsync(IFeature feature)
    {
        if (feature is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (feature is IDisposable disposable)
            disposable.Dispose();
    }
}

public sealed class FeatureReleaseManager : IAsyncDisposable
{
    private const int MaximumReleaseFiles = 256;
    private const long MaximumReleaseBytes = 67_108_864;
    private static readonly TimeSpan MaximumDrainDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MaximumStagingDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumStagingDisposal = TimeSpan.FromSeconds(5);
    private static readonly Assembly[] SharedAssemblies =
    [
        typeof(IFeature).Assembly,
        typeof(IGmailMessageReader).Assembly,
        typeof(ISalesforceRecordReader).Assembly
    ];
    private readonly IServiceProvider _services;
    private readonly IFeatureHostRecycle _recycle;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<ReleaseDigest, ReleaseSlot> _releases = [];
    private readonly Dictionary<FeatureInstallationId, ReleaseSlot> _installations = [];
    private readonly string _cacheRoot;
    private int _recycleRequested;
    private long _snapshotSequence;
    private bool _disposed;

    public FeatureReleaseManager(
        IServiceProvider services,
        IFeatureHostRecycle recycle,
        string? cacheDirectory = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _recycle = recycle ?? throw new ArgumentNullException(nameof(recycle));
        _cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            cacheDirectory ?? Path.Combine(
                Path.GetTempPath(),
                "digitalbrain-feature-host",
                Guid.NewGuid().ToString("N"))));
        Directory.CreateDirectory(_cacheRoot);
        if (File.GetAttributes(_cacheRoot).HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException("The Feature cache directory cannot be a filesystem link.", nameof(cacheDirectory));
    }

    public async Task ActivateAsync(
        FeatureInstallationId installationId,
        FeatureReleaseDescriptor release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ReleaseSlot? retirement = null;
        await _mutation.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ReleaseSlot? current;
            lock (_gate)
            {
                _installations.TryGetValue(installationId, out current);
                if (current?.Digest == release.Digest)
                    return;
            }

            var staged = GetOrLoad(release);
            ReleaseSlot? retired;
            lock (_gate)
            {
                _installations.TryGetValue(installationId, out retired);
                staged.AddInstallation();
                _installations[installationId] = staged;
                retired?.RemoveInstallation();
            }

            if (retired is not null && retired.ActiveInstallations == 0)
            {
                lock (_gate)
                    _releases.Remove(retired.Digest);
                retirement = retired;
            }
        }
        finally
        {
            _mutation.Release();
        }

        if (retirement is not null)
            await RetireAsync(retirement);
    }

    public async Task LoadActiveAsync(
        IReadOnlyList<FeatureActiveInstallation> installations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        var unique = new HashSet<FeatureInstallationId>();
        foreach (var installation in installations)
        {
            if (installation is null || !unique.Add(installation.InstallationId))
                throw new ArgumentException("Active installations must be non-null and unique.", nameof(installations));
            ArgumentNullException.ThrowIfNull(installation.Release);
        }

        foreach (var installation in installations)
            await ActivateAsync(installation.InstallationId, installation.Release, cancellationToken);
    }

    public FeatureReleaseLease Acquire(FeatureInstallationId installationId) =>
        Acquire(installationId, expectedDigest: null);

    public FeatureReleaseLease Acquire(
        FeatureInstallationId installationId,
        ReleaseDigest expectedDigest) =>
        Acquire(installationId, (ReleaseDigest?)expectedDigest);

    private FeatureReleaseLease Acquire(
        FeatureInstallationId installationId,
        ReleaseDigest? expectedDigest)
    {
        ReleaseSlot slot;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_installations.TryGetValue(installationId, out slot!))
                throw new KeyNotFoundException($"Feature installation '{installationId}' is not active.");
            if (expectedDigest is not null && slot.Digest != expectedDigest.Value)
                throw new FeatureReleaseUnavailableException(installationId, expectedDigest.Value);
            slot.Acquire();
        }

        try
        {
            return new FeatureReleaseLease(slot, CreateFeature(slot.FeatureType));
        }
        catch
        {
            slot.Release();
            throw;
        }
    }

    public ReleaseDigest? GetActiveDigest(FeatureInstallationId installationId)
    {
        lock (_gate)
            return _installations.TryGetValue(installationId, out var slot) ? slot.Digest : null;
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseSlot[] releases = [];
        await _mutation.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (var slot in _installations.Values)
                    slot.RemoveInstallation();
                _installations.Clear();
                releases = _releases.Values.ToArray();
                _releases.Clear();
            }
        }
        finally
        {
            _mutation.Release();
        }


        foreach (var release in releases)
            await RetireAsync(release);
        TryDeleteDirectory(_cacheRoot);
    }

    private ReleaseSlot GetOrLoad(FeatureReleaseDescriptor release)
    {
        lock (_gate)
        {
            if (_releases.TryGetValue(release.Digest, out var existing))
                return existing;
        }

        var loaded = Load(release);
        lock (_gate)
        {
            _releases.Add(release.Digest, loaded);
            return loaded;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ReleaseSlot Load(FeatureReleaseDescriptor release)
    {
        var directory = SnapshotRelease(release);
        try
        {
            var manifest = ReadDocument<FeatureManifestDocument>(
                Path.Combine(directory, "manifest.json"),
                "manifest");
            var scenarios = ReadDocument<FeatureScenarioDocument>(
                Path.Combine(directory, "scenarios.json"),
                "scenario result");

            if (manifest.FeatureTypes is null || manifest.FeatureTypes.Count != 1)
                throw new FeatureReleaseValidationException("A release must declare exactly one Feature type.");
            var sdkVersion = typeof(IFeature).Assembly.GetName().Version?.ToString();
            if (!string.Equals(manifest.SdkVersion, sdkVersion, StringComparison.Ordinal))
                throw new FeatureReleaseValidationException("The release targets an incompatible Feature SDK version.");
            if (scenarios.Total <= 0 || scenarios.Passed != scenarios.Total ||
                scenarios.Failed != 0 || scenarios.Skipped != 0)
                throw new FeatureReleaseValidationException("All compiled Feature scenarios must pass before loading.");
            if (string.IsNullOrWhiteSpace(manifest.ImplementationAssembly) ||
                !manifest.ImplementationAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(manifest.ImplementationAssembly), manifest.ImplementationAssembly, StringComparison.Ordinal))
                throw new FeatureReleaseValidationException("The implementation assembly must be a DLL file name.");
            var implementationPath = Path.Combine(directory, "implementation", manifest.ImplementationAssembly);
            if (!File.Exists(implementationPath))
                throw new FeatureReleaseValidationException("The implementation assembly is missing.");

            return LoadImplementationBounded(
                release.Digest,
                directory,
                implementationPath,
                manifest.FeatureTypes[0]);
        }
        catch (FeatureStagingTimeoutException exception)
        {
            RequestRecycle();
            throw new FeatureReleaseValidationException(
                "Feature staging exceeded its deadline and requires host recycling.",
                exception);
        }
        catch (FeatureStagingException exception)
        {
            var reference = exception.BeginUnload();
            if (!ProveUnload(reference))
                RequestRecycle();
            else
                TryDeleteDirectory(directory);
            throw new FeatureReleaseValidationException(
                "The Feature implementation could not be staged.",
                exception.Failure);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private ReleaseSlot LoadImplementationBounded(
        ReleaseDigest digest,
        string directory,
        string implementationPath,
        string featureTypeName)
    {
        var staging = Task.Run(() =>
            LoadImplementation(digest, directory, implementationPath, featureTypeName));
        try
        {
            return staging.WaitAsync(MaximumStagingDuration).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            _ = staging.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                        _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new FeatureStagingTimeoutException();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ReleaseSlot LoadImplementation(
        ReleaseDigest digest,
        string directory,
        string implementationPath,
        string featureTypeName)
    {
        var context = new FeatureReleaseLoadContext(implementationPath, SharedAssemblies);
        try
        {
            var assembly = context.LoadFromAssemblyPath(implementationPath);
            var featureTypes = assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(IFeature).IsAssignableFrom(type))
                .ToArray();
            var featureType = featureTypes.Length == 1 &&
                string.Equals(featureTypes[0].FullName, featureTypeName, StringComparison.Ordinal)
                ? featureTypes[0]
                : throw new FeatureReleaseValidationException("The compiled Feature type does not match the manifest.");
            ValidateConstructor(featureType);
            DisposeStaged(CreateStagedFeature(featureType));
            return new ReleaseSlot(digest, directory, context, featureType);
        }
        catch (Exception exception)
        {
            throw new FeatureStagingException(context, exception);
        }
    }

    private void ValidateConstructor(Type featureType)
    {
        var constructors = featureType.GetConstructors();
        if (constructors.Length != 1)
            throw new FeatureReleaseValidationException("A Feature type must have exactly one public constructor.");
        foreach (var parameter in constructors[0].GetParameters())
        {
            if (!SharedAssemblies.Contains(parameter.ParameterType.Assembly))
                throw new FeatureReleaseValidationException(
                    $"Feature dependency '{parameter.ParameterType.FullName}' is not a shared contract.");
        }
    }

    private IFeature CreateFeature(Type featureType)
    {
        var constructor = featureType.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => _services.GetService(parameter.ParameterType)
                ?? throw new FeatureReleaseValidationException(
                    $"Feature dependency '{parameter.ParameterType.FullName}' is unavailable."))
            .ToArray();
        return (IFeature)constructor.Invoke(arguments);
    }

    private string SnapshotRelease(FeatureReleaseDescriptor release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(release.ReleaseDirectory);
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(release.ReleaseDirectory));
        if (!Directory.Exists(source) || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw new FeatureReleaseValidationException("The release directory is unavailable or linked.");
        var snapshot = Path.Combine(
            _cacheRoot,
            $"{release.Digest.Value}-{Interlocked.Increment(ref _snapshotSequence):D8}");
        var staging = snapshot + ".tmp";
        try
        {
            Directory.CreateDirectory(staging);
            var sourceFiles = EnumerateReleaseFiles(source);
            var sourceBytes = sourceFiles.Sum(path => new FileInfo(path).Length);
            if (sourceBytes > MaximumReleaseBytes)
                throw new FeatureReleaseValidationException("The release exceeds its byte budget.");
            foreach (var path in sourceFiles)
            {
                var relative = Path.GetRelativePath(source, path);
                var destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(path, destination, overwrite: false);
            }

            ValidateRelease(release with { ReleaseDirectory = staging });
            Directory.Move(staging, snapshot);
            return snapshot;
        }
        catch (FeatureReleaseValidationException)
        {
            TryDeleteDirectory(staging);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(staging);
            throw new FeatureReleaseValidationException("The release could not be staged into host-owned storage.", exception);
        }
    }

    private static string ValidateRelease(FeatureReleaseDescriptor release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(release.ReleaseDirectory);
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(release.ReleaseDirectory));
        if (!Directory.Exists(directory))
            throw new FeatureReleaseValidationException("The release directory does not exist.");
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            throw new FeatureReleaseValidationException("Release directories cannot be filesystem links.");
        var files = EnumerateReleaseFiles(directory);
        if (files.Length is 0 or > MaximumReleaseFiles)
            throw new FeatureReleaseValidationException("The release file count is invalid.");
        long totalBytes = 0;
        foreach (var path in files)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new FeatureReleaseValidationException("Release files cannot be filesystem links.");
            totalBytes = checked(totalBytes + new FileInfo(path).Length);
            if (totalBytes > MaximumReleaseBytes)
                throw new FeatureReleaseValidationException("The release exceeds its byte budget.");
        }

        var digestPath = Path.Combine(directory, "digest.txt");
        if (!File.Exists(digestPath) ||
            !string.Equals(File.ReadAllText(digestPath), release.Digest.Value, StringComparison.Ordinal))
            throw new FeatureReleaseValidationException("The release digest marker does not match.");
        var actualDigest = ComputeDigest(directory, files.Where(path =>
            !string.Equals(path, digestPath, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)));
        if (!string.Equals(actualDigest, release.Digest.Value, StringComparison.Ordinal))
            throw new FeatureReleaseValidationException("The release content digest does not match.");
        return directory;
    }

    private static string[] EnumerateReleaseFiles(string directory)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new FeatureReleaseValidationException("Release contents cannot contain filesystem links.");
                if (attributes.HasFlag(FileAttributes.Directory))
                    pending.Push(path);
                else
                {
                    files.Add(path);
                    if (files.Count > MaximumReleaseFiles)
                        throw new FeatureReleaseValidationException("The release file count is invalid.");
                }
            }
        }

        return files.ToArray();
    }

    private static T ReadDocument<T>(string path, string name) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllBytes(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new FeatureReleaseValidationException($"The Feature {name} is empty.");
        }
        catch (FeatureReleaseValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new FeatureReleaseValidationException($"The Feature {name} is invalid.", exception);
        }
    }

    private static string ComputeDigest(string directory, IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var path in files.OrderBy(
                     path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                     StringComparer.Ordinal))
        {
            var relativePath = Encoding.UTF8.GetBytes(
                Path.GetRelativePath(directory, path).Replace('\\', '/'));
            var content = File.ReadAllBytes(path);
            BinaryPrimitives.WriteInt64BigEndian(length, relativePath.Length);
            hash.AppendData(length);
            hash.AppendData(relativePath);
            BinaryPrimitives.WriteInt64BigEndian(length, content.Length);
            hash.AppendData(length);
            hash.AppendData(content);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task RetireAsync(ReleaseSlot release)
    {
        try
        {
            await release.Drained.WaitAsync(MaximumDrainDuration);
        }
        catch (TimeoutException)
        {
            RequestRecycle();
            return;
        }

        if (!release.CanUnload)
        {
            RequestRecycle();
            return;
        }

        var (reference, snapshot) = release.BeginUnload();
        if (!ProveUnload(reference))
        {
            RequestRecycle();
            return;
        }

        TryDeleteDirectory(snapshot);
    }

    private static bool ProveUnload(WeakReference reference)
    {
        for (var attempt = 0; reference.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !reference.IsAlive;
    }

    private static void DisposeStaged(IFeature feature)
    {
        var disposal = Task.Run(async () =>
        {
            if (feature is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (feature is IDisposable disposable)
                disposable.Dispose();
        });
        if (!disposal.Wait(MaximumStagingDisposal))
            throw new TimeoutException("Staged Feature disposal did not complete within its deadline.");
        disposal.GetAwaiter().GetResult();
    }

    private IFeature CreateStagedFeature(Type featureType)
    {
        var construction = Task.Run(() => CreateFeature(featureType));
        if (!construction.Wait(MaximumStagingDisposal))
            throw new TimeoutException("Staged Feature construction did not complete within its deadline.");
        return construction.GetAwaiter().GetResult();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RequestRecycle()
    {
        if (Interlocked.Exchange(ref _recycleRequested, 1) == 0)
            _recycle.RequestRecycle();
    }

    private sealed class FeatureManifestDocument
    {
        public string? ImplementationAssembly { get; init; }
        public string? SdkVersion { get; init; }
        public IReadOnlyList<string>? FeatureTypes { get; init; }
    }

    private sealed class FeatureScenarioDocument
    {
        public int Total { get; init; }
        public int Passed { get; init; }
        public int Failed { get; init; }
        public int Skipped { get; init; }
    }

    private sealed class FeatureStagingException : Exception
    {
        private FeatureReleaseLoadContext? _context;

        public FeatureStagingException(FeatureReleaseLoadContext context, Exception failure)
            : base("Feature staging failed.")
        {
            _context = context;
            Failure = failure is FeatureReleaseValidationException validation
                ? new FeatureReleaseValidationException(validation.Message)
                : new FeatureReleaseValidationException("The Feature implementation failed during staging.");
        }

        public Exception Failure { get; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public WeakReference BeginUnload()
        {
            var context = Interlocked.Exchange(ref _context, null)
                ?? throw new InvalidOperationException("The staging context was already detached.");
            var reference = new WeakReference(context, trackResurrection: true);
            context.Unload();
            return reference;
        }
    }

    private sealed class FeatureStagingTimeoutException : Exception
    {
    }
}

internal sealed class ReleaseSlot
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FeatureReleaseLoadContext? _context;
    private Type? _featureType;
    private int _activeInstallations;
    private int _inFlight;
    private bool _canUnload = true;

    internal ReleaseSlot(
        ReleaseDigest digest,
        string snapshotDirectory,
        FeatureReleaseLoadContext context,
        Type featureType)
    {
        Digest = digest;
        SnapshotDirectory = snapshotDirectory;
        _context = context;
        _featureType = featureType;
    }

    internal ReleaseDigest Digest { get; }
    internal string SnapshotDirectory { get; }
    internal Type FeatureType => _featureType ?? throw new ObjectDisposedException(nameof(ReleaseSlot));
    internal int ActiveInstallations
    {
        get
        {
            lock (_gate)
                return _activeInstallations;
        }
    }
    internal Task Drained => _drained.Task;
    internal bool CanUnload
    {
        get
        {
            lock (_gate)
                return _canUnload;
        }
    }

    internal void AddInstallation()
    {
        lock (_gate)
            _activeInstallations++;
    }

    internal void RemoveInstallation()
    {
        lock (_gate)
        {
            if (_activeInstallations <= 0)
                throw new InvalidOperationException("The release has no active installation to remove.");
            _activeInstallations--;
            CompleteDrain();
        }
    }

    internal void Acquire()
    {
        lock (_gate)
        {
            if (_context is null)
                throw new ObjectDisposedException(nameof(ReleaseSlot));
            _inFlight++;
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (_inFlight <= 0)
                throw new InvalidOperationException("The release has no lease to release.");
            _inFlight--;
            CompleteDrain();
        }
    }

    internal void AbandonLease()
    {
        lock (_gate)
        {
            if (_inFlight <= 0)
                return;
            _inFlight--;
            _canUnload = false;
            CompleteDrain();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal (WeakReference Reference, string Snapshot) BeginUnload()
    {
        lock (_gate)
        {
            if (_activeInstallations != 0 || _inFlight != 0)
                throw new InvalidOperationException("A release cannot unload before it drains.");
            var context = _context ?? throw new ObjectDisposedException(nameof(ReleaseSlot));
            _context = null;
            _featureType = null;
            var reference = new WeakReference(context, trackResurrection: true);
            context.Unload();
            return (reference, SnapshotDirectory);
        }
    }

    private void CompleteDrain()
    {
        if (_activeInstallations == 0 && _inFlight == 0)
            _drained.TrySetResult();
    }
}
