using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Kernel.Tests.Unit;

public sealed class LoginPageFacts
{
    [Fact]
    public async Task Markup_in_the_title_or_message_is_encoded_not_rendered()
    {
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        await LoginPage.WriteAsync(context, "<script>alert(1)</script>", "a \"quoted\" & <b>bold</b> message", StatusCodes.Status200OK);
        var html = System.Text.Encoding.UTF8.GetString(body.ToArray());
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt;</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>&lt;script&gt;alert(1)&lt;/script&gt;</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>a &quot;quoted&quot; &amp; &lt;b&gt;bold&lt;/b&gt; message</p>", html, StringComparison.Ordinal);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }
}
