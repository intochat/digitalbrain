using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class DiagnosticsTests
{
    const string Sample = "neuron A.B\n  using x = synapse(C.D)\n";

    [Theory]
    [InlineData(0, 1, 1)]   // 'n' of "neuron" — start of file
    [InlineData(10, 1, 11)] // the '\n' terminating line 1
    [InlineData(13, 2, 3)]  // 'u' of "using" on line 2
    public void Source_maps_offset_to_line_and_column(int offset, int line, int col)
    {
        var src = new InoSource(Sample);
        src.LineColumn(offset).Should().Be((line, col));
    }

    [Fact]
    public void Source_handles_single_line_input()
    {
        var src = new InoSource("abc");
        src.LineColumn(2).Should().Be((1, 3));
    }

    [Fact]
    public void Bag_collects_errors_and_reports_haserrors()
    {
        var bag = new DiagnosticBag();
        bag.HasErrors.Should().BeFalse();
        bag.Error("E001", "boom", new SourceSpan(0, 1));
        bag.HasErrors.Should().BeTrue();
        bag.Items.Should().ContainSingle(d => d.Code == "E001" && d.Severity == DiagnosticSeverity.Error);
    }
}
