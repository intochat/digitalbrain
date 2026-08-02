using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GmailOpContracts
{
    [Fact(DisplayName =
        "GmailSearchRequest trims Query, defaults MaxResults to 10, and rejects empty query or out-of-range MaxResults")]
    public void SearchRequestValidatesQueryAndMaxResultsBounds()
    {
        var commandId = CommandId.New();
        var request = new GmailSearchRequest("  from:me  ", maxResults: 3, commandId);

        Assert.Equal("from:me", request.Query);
        Assert.Equal(3, request.MaxResults);
        Assert.Equal(commandId, request.CommandId);

        var defaulted = new GmailSearchRequest("is:unread");
        Assert.Equal(10, defaulted.MaxResults);
        Assert.NotEqual(Guid.Empty, defaulted.CommandId.Value);

        var withCommandOnly = new GmailSearchRequest("newer_than:1d", commandId);
        Assert.Equal(10, withCommandOnly.MaxResults);
        Assert.Equal(commandId, withCommandOnly.CommandId);

        Assert.Throws<ArgumentException>(() => new GmailSearchRequest(" "));
        Assert.Throws<ArgumentException>(() => new GmailSearchRequest(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GmailSearchRequest("q", maxResults: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GmailSearchRequest("q", maxResults: 11));
        Assert.Throws<ArgumentException>(() => new GmailSearchRequest("q", 1, default));
    }

    [Fact(DisplayName =
        "GmailSearchResponse Succeeded is true only when Error is null; empty headers are allowed")]
    public void SearchResponseSucceededTracksErrorNull()
    {
        var commandId = CommandId.New();
        var header = new GmailMessageHeader("id-1", "Subject", "sender@example.com");
        var ok = new GmailSearchResponse(commandId, [header]);

        Assert.True(ok.Succeeded);
        Assert.Null(ok.Error);
        Assert.Equal(header, Assert.Single(ok.Headers));

        var failed = new GmailSearchResponse(commandId, [], "  not connected  ");
        Assert.False(failed.Succeeded);
        Assert.Equal("not connected", failed.Error);

        Assert.Throws<ArgumentNullException>(() => new GmailSearchResponse(commandId, null!));
        Assert.Throws<ArgumentException>(() => new GmailSearchResponse(default, []));
    }

    [Fact(DisplayName =
        "GmailGetMessageRequest requires non-empty MessageId and CommandId")]
    public void GetMessageRequestValidatesMessageIdAndCommandId()
    {
        var commandId = CommandId.New();
        var request = new GmailGetMessageRequest("  msg-42  ", commandId);

        Assert.Equal("msg-42", request.MessageId);
        Assert.Equal(commandId, request.CommandId);

        var defaulted = new GmailGetMessageRequest("msg-1");
        Assert.Equal("msg-1", defaulted.MessageId);
        Assert.NotEqual(Guid.Empty, defaulted.CommandId.Value);

        Assert.Throws<ArgumentException>(() => new GmailGetMessageRequest(" "));
        Assert.Throws<ArgumentException>(() => new GmailGetMessageRequest(string.Empty, commandId));
        Assert.Throws<ArgumentException>(() => new GmailGetMessageRequest("msg-1", default));
    }

    [Fact(DisplayName =
        "GmailGetMessageResponse carries optional GmailMessage and Succeeded when Error is null")]
    public void GetMessageResponseSucceededTracksErrorNull()
    {
        var commandId = CommandId.New();
        var message = new GmailMessage("id", "subject", "sender", "body");
        var ok = new GmailGetMessageResponse(commandId, message);

        Assert.True(ok.Succeeded);
        Assert.Null(ok.Error);
        Assert.Same(message, ok.Message);

        var failed = new GmailGetMessageResponse(commandId, null, "  missing  ");
        Assert.False(failed.Succeeded);
        Assert.Null(failed.Message);
        Assert.Equal("missing", failed.Error);

        Assert.Throws<ArgumentException>(() => new GmailGetMessageResponse(default, null));
    }
}
