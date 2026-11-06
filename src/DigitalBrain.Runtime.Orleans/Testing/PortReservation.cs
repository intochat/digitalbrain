using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DigitalBrain.Runtime.Orleans.Testing;

/// <summary>
/// Cross-process synchronized port reservation ring buffer for Orleans silo endpoints.
/// Prevents port collisions between parallel test runs by acquiring named mutex locks,
/// verifying TCP listener availability, and taking per-port lock files in the temporary folder.
/// </summary>
public static class PortReservation
{
    private const string GlobalMutexName = @"Global\DigitalBrain_PortReservation_Mutex";
    private const string LocalMutexName = @"Local\DigitalBrain_PortReservation_Mutex";

    /// <summary>The lowest port reserved for dynamic Orleans endpoints.</summary>
    public const int MinPort = 21000;

    /// <summary>The highest port reserved for dynamic Orleans endpoints.</summary>
    public const int MaxPort = 29000;

    private static readonly object ProcessLock = new();
    private static int _nextPort = MinPort;

    private static string LockDirectoryPath => Path.Combine(Path.GetTempPath(), "digitalbrain-port-locks");

    /// <summary>
    /// Reserves a pair of distinct available ports (SiloPort, GatewayPort) wrapped in a <see cref="PortLease"/>.
    /// </summary>
    /// <returns>A lease containing the reserved Silo and Gateway ports.</returns>
    public static PortLease ReservePair()
    {
        lock (ProcessLock)
        {
            using var crossProcessLock = CrossProcessMutexLock.Acquire();
            var directory = LockDirectoryPath;
            Directory.CreateDirectory(directory);

            var activeListeners = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .ToHashSet();

            var ports = new int[2];
            var lockFiles = new FileStream[2];

            for (var i = 0; i < 2; i++)
            {
                var reserved = false;
                var attempts = 0;
                var totalPortsInRange = MaxPort - MinPort + 1;

                while (!reserved && attempts < totalPortsInRange)
                {
                    var candidate = _nextPort++;
                    if (_nextPort > MaxPort)
                    {
                        _nextPort = MinPort;
                    }
                    attempts++;

                    if (i == 1 && candidate == ports[0])
                    {
                        continue;
                    }

                    if (activeListeners.Contains(candidate))
                    {
                        continue;
                    }

                    if (!IsTcpPortAvailable(candidate))
                    {
                        continue;
                    }

                    var lockPath = Path.Combine(directory, $"{candidate}.lock");
                    var file = TryLock(lockPath);
                    if (file is null)
                    {
                        continue;
                    }

                    ports[i] = candidate;
                    lockFiles[i] = file;
                    reserved = true;
                }

                if (!reserved)
                {
                    for (var cleanup = 0; cleanup < i; cleanup++)
                    {
                        lockFiles[cleanup].Dispose();
                        var lockPath = Path.Combine(directory, $"{ports[cleanup]}.lock");
                        try { File.Delete(lockPath); } catch { }
                    }
                    throw new InvalidOperationException($"Unable to allocate an available port in range {MinPort}-{MaxPort}.");
                }
            }

            return new PortLease(ports[0], ports[1], lockFiles);
        }
    }

    private static bool IsTcpPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static FileStream? TryLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class CrossProcessMutexLock : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _hasHandle;

        private CrossProcessMutexLock(Mutex mutex, bool hasHandle)
        {
            _mutex = mutex;
            _hasHandle = hasHandle;
        }

        public static CrossProcessMutexLock Acquire()
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, GlobalMutexName);
            }
            catch (Exception)
            {
                mutex = new Mutex(false, LocalMutexName);
            }

            var hasHandle = false;
            try
            {
                try
                {
                    hasHandle = mutex.WaitOne(TimeSpan.FromSeconds(30), false);
                }
                catch (AbandonedMutexException)
                {
                    hasHandle = true;
                }

                if (!hasHandle)
                {
                    throw new TimeoutException("Timed out waiting for DigitalBrain port reservation mutex.");
                }

                return new CrossProcessMutexLock(mutex, true);
            }
            catch
            {
                if (hasHandle)
                {
                    mutex.ReleaseMutex();
                }
                mutex.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_hasHandle)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
            _mutex.Dispose();
        }
    }
}

/// <summary>
/// An active lease of reserved Silo and Gateway ports.
/// Releasing or disposing this lease unlocks the ports for future reservations.
/// </summary>
public sealed class PortLease : IDisposable
{
    private readonly FileStream[] _locks;
    private int _disposed;

    internal PortLease(int siloPort, int gatewayPort, FileStream[] locks)
    {
        SiloPort = siloPort;
        GatewayPort = gatewayPort;
        _locks = locks;
    }

    /// <summary>The port leased for the Orleans Silo endpoint.</summary>
    public int SiloPort { get; }

    /// <summary>The port leased for the Orleans Gateway endpoint.</summary>
    public int GatewayPort { get; }

    /// <summary>Deconstructs into SiloPort and GatewayPort.</summary>
    public void Deconstruct(out int siloPort, out int gatewayPort)
    {
        siloPort = SiloPort;
        gatewayPort = GatewayPort;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var file in _locks)
        {
            try
            {
                file.Dispose();
            }
            catch
            {
            }
        }
    }
}
