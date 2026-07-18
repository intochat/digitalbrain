using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TripRadar.Aspire.Tests;

[Collection(AspireDistributedAppCollection.Name)]
public sealed class FlightSearchFlowTests(ITestOutputHelper output)
{
    private static readonly TimeSpan ResourceReadyTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions ClientReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SearchFlightsQuery = """
        query SearchFlights($request: GetFlightsRequest!) {
            flights(request: $request) {
                bestFlights {
                    flights { departureAirport { name code time } arrivalAirport { name code time } duration airplane airline airlineLogo travelClass flightNumber legroom extensions }
                    layovers { duration name id }
                    totalDuration price type airlineLogo bookingToken departureToken
                    carbonEmissions { thisFlight typicalForThisRoute differencePercent }
                }
                otherFlights {
                    flights { departureAirport { name code time } arrivalAirport { name code time } duration airplane airline airlineLogo travelClass flightNumber legroom extensions }
                    layovers { duration name id }
                    totalDuration price type airlineLogo bookingToken departureToken
                    carbonEmissions { thisFlight typicalForThisRoute differencePercent }
                }
                priceInsights { lowestPrice priceLevel typicalPriceRange priceHistory { date price } }
                airports { departure { airport { id name } city country countryCode image thumbnail } arrival { airport { id name } city country countryCode image thumbnail } }
            }
        }
        """;

    [Theory]
    [InlineData("OTP", "PRG")]
    [InlineData("BBU,OTP", "PRG")]
    public async Task SearchFlights_WithMockSerpApi_ReturnsFlightsThatDeserializeIntoMiniAppModel(string departureId, string arrivalId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        await using var telegram = await FakeTelegramServer.StartAsync();
        output.WriteLine($"Fake Telegram listening at {telegram.BaseUrl}");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(
            args: [],
            configureBuilder: (_, hostSettings) =>
            {
                hostSettings.Configuration ??= new ConfigurationManager();
                hostSettings.Configuration["Parameters:telegram-bot-token"] = "test-bot-token";
                hostSettings.Configuration["Parameters:telegram-session-sync-secret"] = "test-session-sync-secret";
            });

        foreach (var resourceName in new[] { "api", "jobs" })
        {
            var resource = (ProjectResource)builder.Resources.Single(r => r.Name == resourceName);
            builder.CreateResourceBuilder(resource).WithEnvironment("MockApi__SerpApi", "true");
        }

        var botResource = (ProjectResource)builder.Resources.Single(r => r.Name == "bot");
        builder
            .CreateResourceBuilder(botResource)
            .WithEnvironment("Bot__TelegramApiBaseUrl", telegram.BaseUrl);

        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token).WaitAsync(ResourceReadyTimeout, cts.Token);

        using var apiClient = app.CreateHttpClient("api");
        apiClient.Timeout = TimeSpan.FromSeconds(60);
        apiClient.DefaultRequestHeaders.Add("X-Client-Type", "api");

        var telegramUserId = 800_000_000L + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000;
        var tokenResponse = await apiClient.PostAsJsonAsync(
            "/api/v1/tokens/dev",
            new { telegramUserId, tier = "advanced" },
            cts.Token);
        tokenResponse.EnsureSuccessStatusCode();

        var loginPayload = await tokenResponse.Content.ReadFromJsonAsync<DevLoginPayload>(ClientReadOptions, cts.Token);
        loginPayload.Should().NotBeNull();
        loginPayload!.Token.Should().NotBeNullOrWhiteSpace("dev login should return a JWT in the body when X-Client-Type: api is set");

        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPayload.Token);

        var payload = new
        {
            query = SearchFlightsQuery,
            variables = new
            {
                request = new
                {
                    flightSearch = new { departureId, arrivalId },
                    advancedOptions = new
                    {
                        type = "RoundTrip",
                        outboundDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        returnDate = DateTime.UtcNow.AddDays(37).ToString("yyyy-MM-dd"),
                        travelClass = "Economy"
                    },
                    passengers = new { adults = 1, children = 0, infantsInSeat = 0 },
                    localization = new { gl = "us", hl = "en", currency = "USD" }
                }
            }
        };

        var graphqlResponse = await apiClient.PostAsJsonAsync("/graphql", payload, cts.Token);
        graphqlResponse.EnsureSuccessStatusCode();

        var rawJson = await graphqlResponse.Content.ReadAsStringAsync(cts.Token);
        output.WriteLine($"GraphQL response for {departureId} -> {arrivalId}: {rawJson}");

        var envelope = JsonSerializer.Deserialize<GraphQlEnvelope<FlightsDataNode>>(rawJson, ClientReadOptions);

        envelope.Should().NotBeNull();
        envelope!.Errors.Should().BeNullOrEmpty("GraphQL should not return errors for a valid search request");
        envelope.Data.Should().NotBeNull();

        var result = envelope.Data!.Flights;
        result.Should().NotBeNull();

        var totalCount = (result!.BestFlights?.Count ?? 0) + (result.OtherFlights?.Count ?? 0);
        totalCount.Should().BeGreaterThan(0, "mock SerpApi should return flights for {0} -> {1}", departureId, arrivalId);

        // Regression guard for the Airport field-name drift. If the client record parameter
        // is renamed away from `Code` (e.g. to `Id`), deserialization silently loses the
        // airport identifier and the Razor layout falls back to garbage (BoardingPass shows
        // the first 3 letters of the name, layover country flags break).
        var firstOption = result.BestFlights?.FirstOrDefault() ?? result.OtherFlights!.First();
        firstOption.Flights.Should().NotBeNullOrEmpty();

        var segment = firstOption.Flights![0];
        segment.DepartureAirport.Should().NotBeNull();
        segment.DepartureAirport!.Name.Should().NotBeNullOrWhiteSpace();
        segment.DepartureAirport.Code.Should().NotBeNullOrWhiteSpace(
            "the GraphQL response field `code` must bind to the MiniApp Airport.Code parameter");
        segment.DepartureAirport.Time.Should().NotBeNullOrWhiteSpace();

        segment.ArrivalAirport.Should().NotBeNull();
        segment.ArrivalAirport!.Code.Should().NotBeNullOrWhiteSpace();

        firstOption.Price.Should().BeGreaterThan(0);
    }

    private sealed record DevLoginPayload(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("refreshToken")] string? RefreshToken);

    private sealed record GraphQlEnvelope<T>(T? Data, List<GraphQlErrorNode>? Errors);

    private sealed record GraphQlErrorNode(string Message);

    private sealed record FlightsDataNode(FlightSearchResult Flights);

    // These mirror the MiniApp client records in TripRadar.MiniApp/Models/Flights/FlightResult.cs.
    // The test fails if the MiniApp contract drifts from the GraphQL response shape.

    private sealed record FlightSearchResult(
        List<FlightOption>? BestFlights,
        List<FlightOption>? OtherFlights);

    private sealed record FlightOption(
        List<FlightSegment>? Flights,
        List<Layover>? Layovers,
        int TotalDuration,
        decimal Price,
        string? Type,
        string? AirlineLogo,
        string? BookingToken,
        string? DepartureToken);

    private sealed record FlightSegment(
        Airport DepartureAirport,
        Airport ArrivalAirport,
        int Duration,
        string? Airplane,
        string? Airline,
        string? AirlineLogo,
        string? TravelClass,
        string? FlightNumber,
        string? Legroom,
        List<string>? Extensions);

    private sealed record Airport(
        string Name,
        string? Code,
        string? Time);

    private sealed record Layover(
        int Duration,
        string? Name,
        string? Id);
}
