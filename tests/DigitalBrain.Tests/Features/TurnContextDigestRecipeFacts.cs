using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TurnContextDigestRecipeFacts
{
    [Fact]
    public void TurnContextDigestMatchesRetiredExecutionRecipe()
    {
        // The retired Execution module's ExecutionContextEntity.DigestOf recipe:
        // var material = string.Concat(delta.SchemaHash, "\0", delta.PayloadJson ?? string.Empty, "\0", delta.BlobRef ?? string.Empty);
        // var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        (string SchemaHash, string? Payload, string? BlobRef)[] cases =
        [
            ("s1", "{\"a\":1}", null),
            ("s1", null, "blob://x")
        ];

        foreach (var (schemaHash, payload, blobRef) in cases)
        {
            var material = string.Concat(schemaHash, "\0", payload ?? string.Empty, "\0", blobRef ?? string.Empty);
            var expectedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
            var context = TurnContexts.For(SignalId.New(), "unused",
                [new ContextRef("some.path", schemaHash, payload, blobRef)], []);
            var actualDigest = Assert.Single(context.Slots, slot => slot.Path == "some.path").Digest;

            Assert.Equal(expectedDigest, actualDigest);
        }
    }
}
