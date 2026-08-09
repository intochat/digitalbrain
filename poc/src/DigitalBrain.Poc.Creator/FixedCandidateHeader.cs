using System;
using System.Text;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

public static class FixedCandidateHeader
{
    public static string Create(CandidateFamilyId family) =>
        string.Join(
            '\n',
            "#:sdk Microsoft.NET.Sdk",
            "#:property TargetFramework=net11.0",
            "#:property OutputType=Library",
            "#:property PublishAot=false",
            "#:property ImplicitUsings=disable",
            $"#:property AssemblyName=DigitalBrain.Poc.Candidate.{family.Value}",
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            "#:project ../../../src/DigitalBrain.Poc.Social.Contracts/DigitalBrain.Poc.Social.Contracts.csproj",
            "#:project ../../../src/DigitalBrain.Poc.Charting.Contracts/DigitalBrain.Poc.Charting.Contracts.csproj",
            string.Empty,
            string.Empty);

    public static byte[] CreateUtf8(CandidateFamilyId family) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Create(family));
}
