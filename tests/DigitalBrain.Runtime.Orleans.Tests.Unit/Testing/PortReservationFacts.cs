using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DigitalBrain.Runtime.Orleans.Testing;

public sealed class PortReservationFacts
{
    [Fact]
    public void ReservePair_ReturnsPortsWithinValidRangeAndDifferent()
    {
        using var lease = PortReservation.ReservePair();
        Assert.InRange(lease.SiloPort, PortReservation.MinPort, PortReservation.MaxPort);
        Assert.InRange(lease.GatewayPort, PortReservation.MinPort, PortReservation.MaxPort);
        Assert.NotEqual(lease.SiloPort, lease.GatewayPort);
    }

    [Fact]
    public void ReservePair_PortsAreNotActiveListeners()
    {
        using var lease = PortReservation.ReservePair();
        var activeListeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port)
            .ToHashSet();

        Assert.DoesNotContain(lease.SiloPort, activeListeners);
        Assert.DoesNotContain(lease.GatewayPort, activeListeners);
    }

    [Fact]
    public async Task ConcurrentReservationsDoNotCollide()
    {
        const int concurrentCount = 10;
        var tasks = Enumerable.Range(0, concurrentCount)
            .Select(_ => Task.Run(() => PortReservation.ReservePair(), TestContext.Current.CancellationToken))
            .ToArray();

        var leases = await Task.WhenAll(tasks);
        try
        {
            var allPorts = leases.SelectMany(l => new[] { l.SiloPort, l.GatewayPort }).ToArray();
            Assert.Equal(allPorts.Length, allPorts.Distinct().Count());
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public void DisposedPortCanBeReusedLater()
    {
        int siloPort;
        int gatewayPort;
        using (var lease = PortReservation.ReservePair())
        {
            siloPort = lease.SiloPort;
            gatewayPort = lease.GatewayPort;
        }

        // Lock files are now released.
        var lockDir = Path.Combine(Path.GetTempPath(), "digitalbrain-port-locks");
        Assert.False(File.Exists(Path.Combine(lockDir, $"{siloPort}.lock")));
        Assert.False(File.Exists(Path.Combine(lockDir, $"{gatewayPort}.lock")));
    }
}
