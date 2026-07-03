using System.Net;
using DigitalBrain.Kernel.Market;

namespace DigitalBrain.Tests.Market;

public class CoinGeckoApiClientTests
{
    private sealed class FakeHttpMessageHandler(string jsonResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task GetBitcoinPriceUsdAsync_parses_coingecko_response_into_formatted_price()
    {
        var handler = new FakeHttpMessageHandler("""{"bitcoin":{"usd":61234.56}}""");
        var httpClient = new HttpClient(handler);
        var client = new CoinGeckoApiClient(httpClient);

        var price = await client.GetBitcoinPriceUsdAsync();

        Assert.Equal("$61,234.56", price);
    }
}
