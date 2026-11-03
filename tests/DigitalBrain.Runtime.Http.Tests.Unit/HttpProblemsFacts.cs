using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime.Http.Surface;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Runtime.Http.Tests.Unit;

public sealed class HttpProblemsFacts
{
    private sealed record SampleBody(string Name);

    [Fact]
    public async Task ReadAsyncRejectsABodyOverTheLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', HttpProblems.MaxBodyBytes + 1)));

        var (body, failure) = await HttpProblems.ReadAsync<SampleBody>(context.Request);

        Assert.Null(body);
        Assert.NotNull(failure);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsAssignableFrom<IStatusCodeHttpResult>(failure).StatusCode);
        var payload = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(failure).Value);
        Assert.Contains(HttpProblems.RequestInvalid, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsyncDeserializesAValidBodyWithinTheLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream("{\"name\":\"ada\"}"u8.ToArray());

        var (body, failure) = await HttpProblems.ReadAsync<SampleBody>(context.Request);

        Assert.Null(failure);
        Assert.Equal(new SampleBody("ada"), body);
    }
}
