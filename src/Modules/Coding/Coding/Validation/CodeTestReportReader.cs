using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DigitalBrain.Coding;

internal static class CodeTestReportReader
{
    public static CodeTestReport Read(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 4 * 1024 * 1024, XmlResolver = null });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "assemblies") { throw new InvalidDataException("Expected an xUnit test report."); }
        var tests = document.Descendants("test").ToArray();
        if (tests.Length == 0 || tests.Any(t => (string?)t.Attribute("result") != "Pass")
            || !tests.Any(t => ((string?)t.Attribute("name"))?.StartsWith("DigitalBrainConformance.", StringComparison.Ordinal) == true)
            || !tests.Any(t => ((string?)t.Attribute("name"))?.StartsWith("DigitalBrainConformance.", StringComparison.Ordinal) == false)
            || document.Descendants("error").Any())
        { throw new InvalidDataException("At least one user test and host conformance must pass, with no failed or skipped tests."); }
        return new(tests.Length, tests.Length, 0, 0, ArtifactStore.Hash(Encoding.UTF8.GetBytes(xml)));
    }
}