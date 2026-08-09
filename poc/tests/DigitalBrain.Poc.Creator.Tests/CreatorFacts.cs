using System;
using System.Linq;
using System.Text;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Runtime;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DigitalBrain.Poc.Creator.Tests;

public sealed class CreatorFacts
{
    private readonly ElonChartSyntaxFactory _creator = new();

    [Fact]
    public void CreatorProducesTheExactNormalModuleShape()
    {
        var result = _creator.Create(ElonChartAuthoringIntent.ForTrustedFixture(
            CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
            "elon-chart",
            "elonmusk"));

        Assert.Single(result.SourceFiles);
        Assert.Equal("elon-chart.cs", result.SourceFiles.Single());
        Assert.Contains("ElonPostMatched", result.Source);
        Assert.Contains("ElonPostRuleNeuron", result.Source);
        Assert.Contains("ChartForwarderNeuron", result.Source);
        Assert.DoesNotContain("ChartNeuron", result.Source);
        Assert.DoesNotContain("ScriptedNeuron", result.Source);
        Assert.Contains(
            "AssemblyName=DigitalBrain.Poc.Candidate.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa",
            result.Source);
        Assert.Contains(
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            result.Source);
        Assert.Contains(
            "#:project ../../../src/DigitalBrain.Poc.Social.Contracts/DigitalBrain.Poc.Social.Contracts.csproj",
            result.Source);
        Assert.Contains(
            "#:project ../../../src/DigitalBrain.Poc.Charting.Contracts/DigitalBrain.Poc.Charting.Contracts.csproj",
            result.Source);
        Assert.DoesNotContain("record SocialPostObserved", result.Source);
        Assert.DoesNotContain("record AddChartPoint", result.Source);
        Assert.DoesNotContain("record ChartPointDraft", result.Source);
    }

    [Fact]
    public void CreatorEmitsTheApprovedDurableContractWithContiguousSerializerIds()
    {
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var source = _creator.Create(ElonChartAuthoringIntent.DefaultTrustedFixture).Source;
        var root = CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var matched = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "ElonPostMatched");
        var state = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "ElonPostRuleState");

        Assert.Equal(
            ["PostId", "OccurredAt", "RuleOrdinal"],
            matched.ParameterList!.Parameters.Select(parameter => parameter.Identifier.ValueText));
        Assert.Equal(
            ["0", "1", "2"],
            matched.ParameterList.Parameters.Select(SerializerId));
        Assert.Equal(
            ["AcceptedCount"],
            state.ParameterList!.Parameters.Select(parameter => parameter.Identifier.ValueText));
        Assert.Equal(["0"], state.ParameterList.Parameters.Select(SerializerId));
        Assert.Equal($"db.poc.family.{family.Value}.matched.v1", Alias(matched));
        Assert.Equal($"db.poc.family.{family.Value}.state.v1", Alias(state));
    }

    [Fact]
    public void SameIntentProducesTheSameSourceAndHash()
    {
        var one = _creator.Create(ElonChartAuthoringIntent.DefaultTrustedFixture);
        var two = _creator.Create(ElonChartAuthoringIntent.DefaultTrustedFixture);

        Assert.Equal(one.Source, two.Source);
        Assert.Equal(one.SourceHash, two.SourceHash);
    }

    [Fact]
    public void IntentBindsTheAttestedTriggerAndLocalSchemaVersion()
    {
        var intent = ElonChartAuthoringIntent.ForTrustedFixture(
            CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
            CandidateSemanticPolicy.SocialPostObservedAlias,
            "elon-chart",
            "elonmusk",
            7);

        var candidate = _creator.Create(intent);
        var validation = new CandidateSourceValidator().Validate(intent, candidate.Source);

        Assert.Equal(CandidateSemanticPolicy.SocialPostObservedAlias, intent.AttestedTriggerAlias);
        Assert.Contains(".v7", candidate.Source);
        Assert.True(validation.IsValid, validation.Detail);
    }

    [Fact]
    public void CreatorOutputPassesThePolicyGate()
    {
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var candidate = _creator.Create(intent);

        var result = new CandidateSourceValidator().Validate(intent, candidate.Source);

        Assert.True(result.IsValid, result.Detail);
        Assert.Equal(CandidatePolicyError.None, result.Error);
    }

    [Fact]
    public void PersistedUtf8SourcePassesThePolicyGateWithoutABom()
    {
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var candidate = _creator.Create(intent);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(candidate.Source);

        var result = new CandidateSourceValidator().Validate(intent, bytes);

        Assert.True(result.IsValid, result.Detail);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    private static string SerializerId(ParameterSyntax parameter) =>
        parameter.AttributeLists
            .SelectMany(list => list.Attributes)
            .Single(attribute => attribute.Name.ToString() == "Id")
            .ArgumentList!.Arguments.Single().Expression.ToString();

    private static string Alias(TypeDeclarationSyntax declaration) =>
        ((LiteralExpressionSyntax)declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Single(attribute => attribute.Name.ToString() == "Alias")
            .ArgumentList!.Arguments.Single().Expression).Token.ValueText;
}
