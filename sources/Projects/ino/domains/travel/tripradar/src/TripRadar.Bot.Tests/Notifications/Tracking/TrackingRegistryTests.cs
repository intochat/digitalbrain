using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;

namespace TripRadar.Bot.Tests.Notifications.Tracking;

public class TrackingRegistryTests
{
    private readonly TrackingRegistry _sut = new();

    [Fact]
    public void RegisterUser_AssociatesChatId()
    {
        _sut.RegisterUser("alice", 123L);

        _sut.TryGetChatId("alice", out var chatId).Should().BeTrue();
        chatId.Should().Be(123L);
    }

    [Fact]
    public void RegisterUser_IgnoresInvalidInputs()
    {
        _sut.RegisterUser("", 1L);
        _sut.RegisterUser("alice", 0L);

        _sut.TryGetChatId("alice", out _).Should().BeFalse();
    }

    [Fact]
    public void Snapshot_PerServiceType_StoredIndependently()
    {
        var flightSnap = new TrackingSnapshot("alice", 1, ServiceType.Flight, Guid.NewGuid(), "100.50", DateTimeOffset.UtcNow);
        var hotelSnap = new TrackingSnapshot("alice", 1, ServiceType.Hotel, Guid.NewGuid(), "200.75", DateTimeOffset.UtcNow);

        _sut.UpsertSnapshot(flightSnap);
        _sut.UpsertSnapshot(hotelSnap);

        _sut.TryGetSnapshot("alice", ServiceType.Flight, out var f).Should().BeTrue();
        _sut.TryGetSnapshot("alice", ServiceType.Hotel, out var h).Should().BeTrue();
        f.Payload.Should().Be("100.50");
        h.Payload.Should().Be("200.75");
    }

    [Fact]
    public void UpsertSnapshot_ReplacesPrevious()
    {
        var first = new TrackingSnapshot("alice", 1, ServiceType.Flight, Guid.NewGuid(), "100", DateTimeOffset.UtcNow);
        var second = first with { Payload = "200" };

        _sut.UpsertSnapshot(first);
        _sut.UpsertSnapshot(second);

        _sut.TryGetSnapshot("alice", ServiceType.Flight, out var snap).Should().BeTrue();
        snap.Payload.Should().Be("200");
    }

    [Fact]
    public void RemoveSnapshot_DropsOnlyMatchingType()
    {
        _sut.UpsertSnapshot(new("alice", 1, ServiceType.Flight, Guid.NewGuid(), "x", DateTimeOffset.UtcNow));
        _sut.UpsertSnapshot(new("alice", 1, ServiceType.Hotel, Guid.NewGuid(), "y", DateTimeOffset.UtcNow));

        _sut.RemoveSnapshot("alice", ServiceType.Flight);

        _sut.TryGetSnapshot("alice", ServiceType.Flight, out _).Should().BeFalse();
        _sut.TryGetSnapshot("alice", ServiceType.Hotel, out _).Should().BeTrue();
    }

    [Fact]
    public void Username_LookupIsCaseInsensitive()
    {
        _sut.RegisterUser("Alice", 99L);

        _sut.TryGetChatId("alice", out var chat).Should().BeTrue();
        chat.Should().Be(99L);
    }
}
