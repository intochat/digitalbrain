using Google.Protobuf;
using Ino.Gateway.Grpc.Services;
using Xunit;

namespace Ino.Hosting.Tests;

/// <summary>
/// Verifies the wire-write path strips CR (0x0D) bytes from <c>RfwDescription</c>
/// and <c>RfwData</c> before they reach the client. The Dart-side
/// <c>parseLibraryFile</c> tokeniser (<c>package:rfw</c> 1.1.x) only accepts
/// SPACE and LF as whitespace and throws <c>ParserException</c> on raw <c>\r</c>;
/// stripping server-side lets neuron authors use platform-default line endings
/// (e.g. <c>StringBuilder.AppendLine</c> on Windows) without producing DSL the
/// client refuses to parse. See <c>docs/rfw-research-notes.md</c> R2.
/// </summary>
public sealed class ChatRfwSerializationTests
{
    [Fact]
    public void StripCarriageReturns_removes_all_CR_bytes()
    {
        var input = "widget root = Column(\r\n  children: []\r\n);"u8.ToArray();

        var result = InoGrpcService.StripCarriageReturns(input);

        Assert.DoesNotContain((byte)'\r', result.ToByteArray());
        // LFs survive — they're the only legal line terminator for the parser.
        Assert.Contains((byte)'\n', result.ToByteArray());
    }

    [Fact]
    public void StripCarriageReturns_returns_unchanged_when_no_CR_present()
    {
        var input = "widget root = Column();\n"u8.ToArray();

        var result = InoGrpcService.StripCarriageReturns(input);

        Assert.Equal(input, result.ToByteArray());
    }

    [Fact]
    public void StripCarriageReturns_preserves_byte_order()
    {
        // Bytes 0x01..0x05 with CRs sprinkled in between — ordering must be stable.
        var input = new byte[] { 0x01, (byte)'\r', 0x02, 0x03, (byte)'\r', 0x04, 0x05 };

        var result = InoGrpcService.StripCarriageReturns(input).ToByteArray();

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, result);
    }
}
