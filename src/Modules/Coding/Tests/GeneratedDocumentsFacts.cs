using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class GeneratedDocumentsFacts
{
    [Theory]
    [InlineData("E:/repo/src/A/obj/Debug/net11.0/Thing.cs", true)]
    [InlineData(@"E:\repo\src\A\obj\Debug\net11.0\Thing.cs", true)]
    [InlineData("E:/repo/src/A/bin/Release/net11.0/Thing.cs", true)]
    [InlineData(@"E:\repo\src\A\bin\Release\net11.0\Thing.cs", true)]
    [InlineData("E:/repo/src/A/Thing.g.cs", true)]
    [InlineData(@"E:\repo\src\A\Thing.g.cs", true)]
    [InlineData("E:/repo/src/A/Thing.g.i.cs", true)]
    [InlineData("E:/repo/src/A/Thing.generated.cs", true)]
    [InlineData("E:/repo/src/A/Thing.designer.cs", true)]
    [InlineData("E:/repo/src/A/Thing.Designer.cs", true)]
    [InlineData("E:/repo/src/A/Thing.cs", false)]
    [InlineData(@"E:\repo\src\A\Thing.cs", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Path_is_classified_as_generated_or_not(string? path, bool expected)
    {
        Assert.Equal(expected, GeneratedDocuments.IsGenerated(path));
    }
}
