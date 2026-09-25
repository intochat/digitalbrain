using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.Roslyn;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class EditReplyReaderFacts
{
    [Fact]
    public void AnEditArrayInsideProseIsRead()
    {
        var edits = EditReplyReader.Read("""
            Here you go:
            [{"kind":"InsertMember","symbolId":"T:SampleInbox.Inbox","source":"public void Clear() => _messages.Clear();"}]
            """);

        var edit = Assert.Single(edits);
        Assert.Equal(EditKind.InsertMember, edit.Kind);
        Assert.Equal("T:SampleInbox.Inbox", edit.SymbolId);
    }

    [Theory]
    [InlineData("no edits here")]
    [InlineData("[{\"kind\":\"Explode\"}]")]
    public void AnUnreadableReplyIsAFormatException(string reply)
        => Assert.Throws<FormatException>(() => EditReplyReader.Read(reply));
}
