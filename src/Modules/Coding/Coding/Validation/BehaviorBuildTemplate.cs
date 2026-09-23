using System.Security;

namespace DigitalBrain.Coding;

internal static class BehaviorBuildTemplate
{
    public const string Version = "behavior-template-1";

    public static string Project(IEnumerable<string> references, bool tests)
    {
        var paths = references.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var assemblies = string.Join('\n', paths.Select(path =>
            $"<Reference Include=\"{SecurityElement.Escape(Path.GetFileNameWithoutExtension(path))}\"><HintPath>{SecurityElement.Escape(Path.GetFullPath(path))}</HintPath><Private>true</Private></Reference>"));
        var testing = tests ? "<IsTestProject>true</IsTestProject><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner><TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>" : "";
        var package = tests ? "<PackageReference Include=\"xunit.v3.mtp-v2\" Version=\"4.0.0\" />" : "";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework><OutputType>Exe</OutputType>
                <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LangVersion>preview</LangVersion>
                <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps><ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally><PublishAot>false</PublishAot>
                <UseSharedCompilation>false</UseSharedCompilation><EnableDefaultCompileItems>true</EnableDefaultCompileItems>
                {testing}
              </PropertyGroup>
              <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />{assemblies}{package}</ItemGroup>
            </Project>
            """;
    }

    public const string Conformance = """
        public sealed class DigitalBrainConformance
        {
            [Xunit.Fact]
            public void EntryPoint()
            {
                var assembly = System.Reflection.Assembly.Load("Behavior");
                Xunit.Assert.NotNull(assembly.EntryPoint);
                Xunit.Assert.Contains(assembly.GetExportedTypes(), t => !t.IsAbstract && t.GetInterfaces().Any(i => i.FullName == "DigitalBrain.Core.IBehavior"));
            }
        }
        """;
}