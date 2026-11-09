using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Identity;

namespace DigitalBrain.Tests.Unit.Identity;

public sealed class CompoundGrainKeyAdversarialFacts
{
    [Fact]
    public void TryParse_WithNullOrEmptyOrWhitespace_ReturnsFalseWithoutThrowing()
    {
        Assert.False(CompoundGrainKey.TryParse(null, out var key1));
        Assert.Equal(default, key1);

        Assert.False(CompoundGrainKey.TryParse(string.Empty, out var key2));
        Assert.Equal(default, key2);

        Assert.False(CompoundGrainKey.TryParse("   ", out var key3));
        Assert.Equal(default, key3);

        Assert.False(CompoundGrainKey.TryParse("\t\r\n", out var key4));
        Assert.Equal(default, key4);
    }

    [Theory]
    [InlineData("alice/apps/   ")]
    [InlineData("alice/apps/\t")]
    [InlineData("alice/apps/  \u00A0  ")]
    [InlineData("@ /type/key")]
    [InlineData("@\u00A0/type/key")]
    public void TryParse_WithWhitespaceKey_MustNotThrowUnhandledException(string input)
    {
        // TryParse contract: MUST return false when input cannot form a valid key,
        // and MUST NEVER leak an ArgumentException or other unhandled exception to the caller.
        var exception = Record.Exception(() =>
        {
            var result = CompoundGrainKey.TryParse(input, out var key);
            // If it returns true or false, it didn't throw
            _ = result;
            _ = key;
        });

        Assert.Null(exception);
    }

    [Fact]
    public void NeuronId_TryParse_ReturnsFalseForWhitespaceKeyWithoutThrowing()
    {
        var exception = Record.Exception(() =>
        {
            var result = NeuronId.TryParse("alice/apps/   ", out var neuronId);
            Assert.False(result);
            Assert.Equal(default, neuronId);
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("tenant/apps/ino/editor/v1", "tenant", "apps", "ino/editor/v1")]
    [InlineData("storage-brain/vfs/work/projects/2026/src/main.cs", "storage-brain", "vfs", "work/projects/2026/src/main.cs")]
    [InlineData("brain1/service/a/b/c/d/e/f/g", "brain1", "service", "a/b/c/d/e/f/g")]
    public void Parse_WithMultiSlashInKeyComponent_ParsesAndRoundtripsCleanly(string raw, string expectedBrain, string expectedType, string expectedKey)
    {
        var compound = CompoundGrainKey.Parse(raw);

        Assert.Equal(expectedBrain, compound.Brain.Value);
        Assert.Equal(expectedType, compound.Type);
        Assert.Equal(expectedKey, compound.Key);
        Assert.Equal(raw, compound.ToString());

        var neuronId = compound.ToNeuronId();
        Assert.Equal(expectedBrain, neuronId.Brain.Value);
        Assert.Equal(expectedType, neuronId.Type);
        Assert.Equal(expectedKey, neuronId.Key);
        Assert.Equal(raw, neuronId.ToString());
    }

    [Theory]
    [InlineData("мозг/нейрон/ключ-1", "мозг", "нейрон", "ключ-1")]
    [InlineData("大脑/应用/计算器", "大脑", "应用", "计算器")]
    [InlineData("ブレイン/タイプ/キー", "ブレイン", "タイプ", "キー")]
    [InlineData("über-brain/grün/schön-öäü", "über-brain", "grün", "schön-öäü")]
    public void Parse_WithUnicodeCharacters_PreservesIntegrity(string raw, string expectedBrain, string expectedType, string expectedKey)
    {
        Assert.True(CompoundGrainKey.TryParse(raw, out var parsed));
        Assert.Equal(expectedBrain, parsed.Brain.Value);
        Assert.Equal(expectedType, parsed.Type);
        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(raw, parsed.ToString());
    }

    [Theory]
    [InlineData("🧠/rocket-🚀/star-✨", "🧠", "rocket-🚀", "star-✨")]
    [InlineData("alice/apps/🔥_test_123", "alice", "apps", "🔥_test_123")]
    public void Parse_WithEmojis_PreservesIntegrity(string raw, string expectedBrain, string expectedType, string expectedKey)
    {
        Assert.True(CompoundGrainKey.TryParse(raw, out var parsed));
        Assert.Equal(expectedBrain, parsed.Brain.Value);
        Assert.Equal(expectedType, parsed.Type);
        Assert.Equal(expectedKey, parsed.Key);
        Assert.Equal(raw, parsed.ToString());
    }

    [Fact]
    public void BrainId_BoundaryLengths_Enforces200CharacterLimit()
    {
        var brain200 = new string('a', 200);
        var validKey = $"{brain200}/apps/mykey";
        Assert.True(CompoundGrainKey.TryParse(validKey, out var parsed200));
        Assert.Equal(brain200, parsed200.Brain.Value);

        var brain201 = new string('a', 201);
        var invalidKey = $"{brain201}/apps/mykey";
        Assert.False(CompoundGrainKey.TryParse(invalidKey, out _));
        Assert.Throws<FormatException>(() => CompoundGrainKey.Parse(invalidKey));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(65_536)]
    public void ExtremelyLongKeys_ParseAndRoundtripSuccessfully(int keyLength)
    {
        var longKeyPart = new string('x', keyLength);
        var raw = $"tenant-1/storage/{longKeyPart}";

        Assert.True(CompoundGrainKey.TryParse(raw, out var parsed));
        Assert.Equal("tenant-1", parsed.Brain.Value);
        Assert.Equal("storage", parsed.Type);
        Assert.Equal(longKeyPart, parsed.Key);
        Assert.Equal(raw, parsed.ToString());

        var neuronId = parsed.ToNeuronId();
        Assert.Equal(longKeyPart, neuronId.Key);
    }

    [Theory]
    [InlineData("alice/apps/sub//key")]   // consecutive slashes in key
    [InlineData("alice/apps/key/")]       // trailing slash in key
    [InlineData("alice/apps//key")]       // leading slash in key
    [InlineData("alice//calc")]           // empty type
    [InlineData("alice///calc")]          // multiple empty segments
    [InlineData("//")]                    // just slashes
    [InlineData("///")]                   // three slashes
    [InlineData("/apps/calc")]            // empty brain
    [InlineData("@/apps/calc")]           // empty system brain
    [InlineData("@@system/apps/calc")]    // double @ in brain
    [InlineData("alice!/apps/calc")]      // exclamation mark in brain
    [InlineData("alice:/apps/calc")]      // colon in brain
    [InlineData("alice\\/apps/calc")]     // backslash in brain
    [InlineData("alice/apps/key\nwithcontrol")] // control char in key
    [InlineData("alice/app\t/key")]       // control char in type
    public void MalformedKeys_TryParseReturnsFalse_ParseThrowsFormatException(string malformed)
    {
        Assert.False(CompoundGrainKey.TryParse(malformed, out var key));
        Assert.Equal(default, key);
        Assert.Throws<FormatException>(() => CompoundGrainKey.Parse(malformed));
    }

    [Fact]
    public void SystemBrain_ParsesCorrectlyAndIdentifiesAsSystem()
    {
        var raw = "@system/registry/settings";
        Assert.True(CompoundGrainKey.TryParse(raw, out var compound));
        Assert.True(compound.Brain.IsSystem);
        Assert.Equal("@system", compound.Brain.Value);
        Assert.Equal("registry", compound.Type);
        Assert.Equal("settings", compound.Key);

        // ToNeuronId creates a NeuronId with the system brain
        var neuronId = compound.ToNeuronId();
        Assert.True(neuronId.Brain.IsSystem);
        Assert.Equal("@system/registry/settings", neuronId.ToString());

        // Note: NeuronId.TryParse explicitly rejects system scopes by design (user input must not name system scopes)
        Assert.False(NeuronId.TryParse(raw, out _));
    }
}
