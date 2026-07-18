using System.Xml.Linq;
using AI.Contracts;
using Brain.Contracts;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class AiContractTests
{
    [Fact]
    public void Contract_project_depends_only_on_brain_contracts()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "modules",
            "AI.Contracts",
            "AI.Contracts.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["../../kernel/Brain.Contracts/Brain.Contracts.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void Text_generation_contract_is_stable()
    {
        Assert.Equal("ai.text.generate.v1", AiCapabilityIds.TextGenerate);

        var method = typeof(ITextGenerationNeuron).GetMethod(nameof(ITextGenerationNeuron.GenerateAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TextGenerationResult>), method.ReturnType);
        Assert.Equal(
            AiCapabilityIds.TextGenerate,
            method.GetCustomAttributes(typeof(NeuronContractAttribute), false)
                .Cast<NeuronContractAttribute>()
                .Single()
                .Contract);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
