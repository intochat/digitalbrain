using System.Reflection;
using DigitalBrain;
using Google.Contracts;
using Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Client;

public sealed class ProviderClientCompilationTests
{
    [Fact]
    public void Compile_time_caller_contains_only_typed_provider_Get()
    {
        var method = typeof(ProviderClientCompilationTests).GetMethod(
            nameof(CompileTimeCaller),
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(DigitalBrainClient), method.GetParameters().Single().ParameterType);
        Assert.Equal(typeof(IGmail), method.ReturnType.GetGenericArguments()[0]);
        Assert.Equal(typeof(ISalesforce), method.ReturnType.GetGenericArguments()[1]);

        var callerBody = ExtractCompileTimeCallerBody(ReadOwnSource());
        Assert.Contains("brain.Get<IGmail>()", callerBody, StringComparison.Ordinal);
        Assert.Contains("brain.Get<ISalesforce>()", callerBody, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(callerBody, "Get<"));
        Assert.DoesNotContain("GetGrain", callerBody, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchProxy", callerBody, StringComparison.Ordinal);
    }

    private static (IGmail Gmail, ISalesforce Salesforce) CompileTimeCaller(DigitalBrainClient brain)
    {
        var gmail = brain.Get<IGmail>();
        var salesforce = brain.Get<ISalesforce>();
        return (gmail, salesforce);
    }

    private static string ExtractCompileTimeCallerBody(string source)
    {
        const string marker = "CompileTimeCaller(DigitalBrainClient brain)";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[(open + 1)..index];
            }
        }

        throw new InvalidOperationException("CompileTimeCaller body was not closed.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadOwnSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tests",
                "DigitalBrain.Tests",
                "Client",
                "ProviderClientCompilationTests.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException("ProviderClientCompilationTests.cs");
    }
}
