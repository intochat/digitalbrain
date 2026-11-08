using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit.Synapses;

public sealed class BannedApiAnalyzerFacts
{
    private const string State = """
        using DigitalBrain;
        [State("week")] public sealed record WeekSpend(decimal Spent = 0);
        """;

    [Fact]
    public Task HttpClientInAUnitIsDB0045()
    {
        var test = Verify<BannedApiAnalyzer>.Create(State + """

            public static class Fetch
            {
                public static object Client() => {|#0:new System.Net.Http.HttpClient()|};
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(0).WithArguments("System.Net.Http.HttpClient..ctor"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task MemoryStreamIsAllowed() => Verify<BannedApiAnalyzer>.Diagnostics(State + """

        public static class Buffer
        {
            public static long Length()
            {
                using System.IO.Stream stream = new System.IO.MemoryStream(new byte[4]);
                return stream.Length;
            }
        }
        """);

    [Fact]
    public Task TaskRunAndUsingStaticFileAreDB0045()
    {
        var test = Verify<BannedApiAnalyzer>.Create("""
            {|#0:using static System.IO.File;|}
            """ + State + """

            public static class Spawn
            {
                public static void Go() => {|#1:System.Threading.Tasks.Task.Run(() => { })|};
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(0).WithArguments("System.IO.File"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(1).WithArguments("System.Threading.Tasks.Task.Run"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task ReflectionAndEnvironmentAreDB0045ButNewLineIsAllowed()
    {
        var test = Verify<BannedApiAnalyzer>.Create(State + """

            public static class Reach
            {
                public static object? Make() => {|#0:System.Activator.CreateInstance({|#1:System.Type.GetType("X")|}!)|};
                public static string? Path() => {|#2:System.Environment.GetEnvironmentVariable("PATH")|};
                public static string Line() => System.Environment.NewLine;
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(0).WithArguments("System.Activator.CreateInstance"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(1).WithArguments("System.Type.GetType"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0045", DiagnosticSeverity.Error).WithLocation(2).WithArguments("System.Environment.GetEnvironmentVariable"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task AnAssemblyWithoutStateIsIgnored() => Verify<BannedApiAnalyzer>.Diagnostics("""
        public static class Fetch
        {
            public static object Client() => new System.Net.Http.HttpClient();
        }
        """);
}
