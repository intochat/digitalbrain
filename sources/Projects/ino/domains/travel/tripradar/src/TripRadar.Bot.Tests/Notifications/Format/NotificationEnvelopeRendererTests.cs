using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Tests.Notifications.Format;

public class NotificationEnvelopeRendererTests
{
    private readonly NotificationEnvelopeRenderer _sut = new();

    [Fact]
    public void Render_FlightExample_MatchesSpecLayout()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Flight,
            RequestSummary: "Париж → Рим, 14 мая",
            MainResult: "Найдена новая цена: €120",
            Details: ["Маршрут: CDG → FCO", "Дата вылета: 14 мая"],
            DeepLinkUrl: "https://app/results");

        var text = _sut.Render(envelope);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
        lines.Should().HaveCount(6);
        lines[0].Should().Be("Новый результат по сохранённому запросу: Перелёт");
        lines[1].Should().Be("Париж → Рим, 14 мая");
        lines[2].Should().Be("Найдена новая цена: €120");
        lines[3].Should().Be("Маршрут: CDG → FCO");
        lines[4].Should().Be("Дата вылета: 14 мая");
        lines[5].Should().Be("Открой TripRadar, чтобы посмотреть детали.");
    }

    [Fact]
    public void Render_HotelExample_MatchesSpecLayout()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Hotel,
            RequestSummary: "Рим, 14-17 мая",
            MainResult: "Найден новый вариант: €210 за 3 ночи",
            Details: ["Район: Centro Storico", "Даты: 14-17 мая"],
            DeepLinkUrl: "https://app/results");

        var text = _sut.Render(envelope);

        text.Should().StartWith("Новый результат по сохранённому запросу: Отель");
        text.Should().Contain("Рим, 14-17 мая");
        text.Should().Contain("€210 за 3 ночи");
        text.Should().EndWith("Открой TripRadar, чтобы посмотреть детали.");
    }

    [Fact]
    public void Render_RestaurantExample_OmitsEmptyDetailLines()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.LocalPlaces,
            RequestSummary: "Рим, итальянская кухня",
            MainResult: "Найден новый вариант",
            Details: ["Рейтинг: 4.7", "", "Район: Trastevere"],
            DeepLinkUrl: "https://app/results");

        var text = _sut.Render(envelope);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
        lines.Should().HaveCount(6);
        lines[0].Should().Be("Новый результат по сохранённому запросу: Ресторан");
        lines[3].Should().Be("Рейтинг: 4.7");
        lines[4].Should().Be("Район: Trastevere");
        lines.Should().NotContain(string.Empty);
        lines.Should().NotContain("N/A");
    }

    [Fact]
    public void Render_EventExample_MatchesSpecLayout()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Event,
            RequestSummary: "Рим, 15 мая",
            MainResult: "Найдено новое событие: Jazz Night",
            Details: ["Место: Auditorium Parco della Musica", "Время: 20:00"],
            DeepLinkUrl: "https://app/results");

        var text = _sut.Render(envelope);

        text.Should().Contain("Новый результат по сохранённому запросу: Событие");
        text.Should().Contain("Найдено новое событие: Jazz Night");
        text.Should().EndWith("Открой TripRadar, чтобы посмотреть детали.");
    }

    [Fact]
    public void Render_TruncatesDetailsAtThreeLines()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: "X",
            RequestSummary: "summary",
            MainResult: "main",
            Details: ["d1", "d2", "d3", "d4", "d5"],
            DeepLinkUrl: "");

        var text = _sut.Render(envelope);

        text.Should().Contain("d1").And.Contain("d2").And.Contain("d3");
        text.Should().NotContain("d4").And.NotContain("d5");
    }

    [Fact]
    public void Render_AlwaysContainsHeaderAndCta()
    {
        var envelope = new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Flight,
            RequestSummary: "x",
            MainResult: "y",
            Details: [],
            DeepLinkUrl: "");

        var text = _sut.Render(envelope);

        text.Should().StartWith(NotificationStrings.Header + ":");
        text.Should().EndWith(NotificationStrings.Cta);
    }
}
