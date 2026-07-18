using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Infrastructure.Providers.Kiwi;
using TripRadar.Server.Infrastructure.Providers.Kiwi.Settings;

namespace TripRadar.Server.Tests.Flights;

public class KiwiFlightPriceCalendarProviderTests
{
    [Fact]
    public async Task GetMonthlyPricesAsync_MapsKiwiPricePerDateResponseAndSendsExpectedQuery()
    {
        Uri? capturedUri = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            var json = """
                {
                  "data": {
                    "itineraryPricesCalendar": {
                      "__typename": "ItineraryPricesCalendar",
                      "calendar": [
                        { "date": "2026-05-04T00:00:00", "ratedPrice": { "price": { "amount": "125" }, "rating": "AVERAGE" } },
                        { "date": "2026-05-05T00:00:00", "ratedPrice": { "price": { "amount": "99" }, "rating": "CHEAP" } },
                        { "date": "2026-05-06T00:00:00", "ratedPrice": { "price": { "amount": "0" }, "rating": "CHEAP" } },
                        { "date": "2026-06-01T00:00:00", "ratedPrice": { "price": { "amount": "80" }, "rating": "AVERAGE" } }
                      ]
                    }
                  }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        });

        var provider = CreateProvider(handler);

        var result = await provider.GetMonthlyPricesAsync(
            new FlightPriceCalendarProviderRequest("SOF", "VIE", 2026, 5, "EUR"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().HaveCount(2);
        result.Value!.Days.Should().Contain(day => day.Date == new DateOnly(2026, 5, 4) && day.LowestPrice == 125m && day.Currency == "EUR");
        result.Value!.Days.Should().Contain(day => day.Date == new DateOnly(2026, 5, 5) && day.LowestPrice == 99m && day.Currency == "EUR");

        capturedUri.Should().NotBeNull();
        capturedUri!.Host.Should().Be("api.skypicker.com");
        capturedUri.AbsolutePath.Should().Be("/umbrella/v2/graphql");
        capturedUri.Query.Should().Be("?featureName=CalendarPricesFetcherQuery");

        capturedBody.Should().NotBeNull();
        using var document = JsonDocument.Parse(capturedBody!);
        var variables = document.RootElement.GetProperty("variables");
        variables.GetProperty("search").GetProperty("source").GetProperty("ids")[0].GetString().Should().Be("SOF");
        variables.GetProperty("search").GetProperty("destination").GetProperty("ids")[0].GetString().Should().Be("VIE");
        variables.GetProperty("search").GetProperty("dates").GetProperty("start").GetString().Should().Be("2026-05-01T00:00:00");
        variables.GetProperty("search").GetProperty("dates").GetProperty("end").GetString().Should().Be("2026-05-31T23:59:59");
        variables.GetProperty("options").GetProperty("currency").GetString().Should().Be("eur");
        variables.GetProperty("options").GetProperty("locale").GetString().Should().Be("en");
        variables.GetProperty("options").GetProperty("market").GetString().Should().Be("bg");
        variables.GetProperty("options").GetProperty("partner").GetString().Should().Be("skypicker");
        variables.GetProperty("filter").GetProperty("contentProviders")[0].GetString().Should().Be("KIWI");
        variables.GetProperty("filter").GetProperty("transportTypes")[0].GetString().Should().Be("FLIGHT");
    }

    [Fact]
    public async Task GetMonthlyPricesAsync_KeepsLowestPriceWhenKiwiReturnsDuplicates()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "data": {
                    "itineraryPricesCalendar": {
                      "calendar": [
                        { "date": "2026-05-05T00:00:00", "ratedPrice": { "price": { "amount": "150" } } },
                        { "date": "2026-05-05T00:00:00", "ratedPrice": { "price": { "amount": "99" } } },
                        { "date": "2026-05-06T00:00:00", "ratedPrice": { "price": { "amount": "175" } } }
                      ]
                    }
                  }
                }
                """)
        });

        var provider = CreateProvider(handler);

        var result = await provider.GetMonthlyPricesAsync(
            new FlightPriceCalendarProviderRequest("SOF", "VIE", 2026, 5, "EUR"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().HaveCount(2);
        result.Value!.Days.Should().Contain(day => day.Date == new DateOnly(2026, 5, 5) && day.LowestPrice == 99m);
        result.Value!.Days.Should().Contain(day => day.Date == new DateOnly(2026, 5, 6) && day.LowestPrice == 175m);
    }

    [Fact]
    public async Task GetMonthlyPricesAsync_ReturnsFailureWhenKiwiFails()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await provider.GetMonthlyPricesAsync(
            new FlightPriceCalendarProviderRequest("SOF", "VIE", 2026, 5, "EUR"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetMonthlyPricesAsync_ReturnsEmptySuccessWhenKiwiHasNoPrices()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "data": { "itineraryPricesCalendar": { "calendar": [] } } }""")
        }));

        var result = await provider.GetMonthlyPricesAsync(
            new FlightPriceCalendarProviderRequest("SOF", "VIE", 2026, 5, "EUR"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().BeEmpty();
    }

    private static KiwiFlightPriceCalendarProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var settings = Options.Create(new KiwiCalendarSettings
        {
            BaseUrl = "https://api.skypicker.com",
            RequestTimeoutSeconds = 30,
            DefaultLocale = "en",
            Market = "bg",
            Partner = "skypicker"
        });

        return new KiwiFlightPriceCalendarProvider(
            client,
            settings,
            NullLogger<KiwiFlightPriceCalendarProvider>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
