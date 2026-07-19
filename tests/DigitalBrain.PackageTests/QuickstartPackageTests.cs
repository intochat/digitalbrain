using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.PackageTests;

[Collection(nameof(PackedFrameworkCollection))]
public sealed class QuickstartPackageTests(PackedFrameworkFixture fixture)
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedPackages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DigitalBrain.Quickstart.AppHost"] = "DigitalBrain.Aspire.Hosting",
            ["DigitalBrain.Quickstart.Kernel"] = "DigitalBrain.Kernel",
            ["DigitalBrain.Quickstart.Console"] = "DigitalBrain.Aspire",
            ["DigitalBrain.Quickstart.OrleansDashboard"] = "DigitalBrain.DevTools",
            ["DigitalBrain.Quickstart.DevUI"] = "DigitalBrain.DevTools"
        };

    private static readonly string[] ExpectedProjects =
    [
        .. ExpectedPackages.Keys,
        "DigitalBrain.Quickstart.TestProvider"
    ];

    [Fact]
    public void Quickstart_projects_consume_framework_packages_without_framework_project_references()
    {
        var quickstartRoot = QuickstartRoot();
        var projectFiles = Directory
            .GetFiles(quickstartRoot, "*.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ExpectedProjects.Order(StringComparer.Ordinal),
            projectFiles
                .Select(Path.GetFileNameWithoutExtension)
                .Order(StringComparer.Ordinal));

        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var project = XDocument.Load(projectFile);
            var packageReferences = project
                .Descendants("PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
            if (ExpectedPackages.TryGetValue(projectName, out var expectedPackage))
                Assert.Contains(expectedPackage, packageReferences);
            else
                Assert.Empty(packageReferences);

            foreach (var projectReference in project.Descendants("ProjectReference"))
            {
                var include = projectReference.Attribute("Include")?.Value;
                Assert.False(string.IsNullOrWhiteSpace(include));
                var referencedPath = Path.GetFullPath(
                    include!,
                    Path.GetDirectoryName(projectFile)!);
                Assert.True(
                    IsUnder(referencedPath, quickstartRoot),
                    $"{projectName} references framework source {referencedPath}.");
            }
        }
    }

    [Fact]
    public void Quickstart_has_no_direct_provider_sdk_dependencies()
    {
        var forbiddenPackages = new[]
        {
            "OpenAI",
            "Anthropic",
            "Azure.AI.OpenAI",
            "Microsoft.Extensions.AI.OpenAI"
        };

        foreach (var projectFile in Directory.GetFiles(
                     QuickstartRoot(),
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var references = XDocument
                .Load(projectFile)
                .Descendants("PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value is not null)
                .Cast<string>();
            Assert.DoesNotContain(
                references,
                reference => forbiddenPackages.Contains(
                    reference,
                    StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Quickstart_restores_and_builds_from_the_local_feed_with_empty_caches()
    {
        var quickstart = fixture.PrepareQuickstart();

        foreach (var packageId in fixture.PackageIds)
        {
            var packageDirectory = Path.Combine(
                quickstart.PackagesDirectory,
                packageId.ToLowerInvariant(),
                fixture.PackageVersion);
            Assert.True(
                Directory.Exists(packageDirectory),
                $"{packageId} was not restored into the isolated package cache.");
            var metadataPath = Path.Combine(packageDirectory, ".nupkg.metadata");
            Assert.True(File.Exists(metadataPath), metadataPath);
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            Assert.Equal(
                Path.GetFullPath(fixture.FeedDirectory),
                Path.GetFullPath(
                    metadata.RootElement.GetProperty("source").GetString()
                    ?? string.Empty));
        }
    }

    [Fact]
    public void Console_startup_creates_owner_session_before_resolving_client()
    {
        var quickstart = fixture.PrepareQuickstart();
        var result = Run(
            quickstart,
            "DigitalBrain.Quickstart.Console",
            "net8.0",
            environment: DevelopmentEnvironment(quickstart),
            "--startup-contract");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("owner-guard:ok", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "owner-session:quickstart-owner",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("client:DigitalBrainClient", result.Output, StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.Console",
            "Program.cs"));
        Assert.DoesNotContain(
            "GetRequiredService<DigitalBrainClient>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Console_commands_have_deterministic_results()
    {
        var quickstart = fixture.PrepareQuickstart();
        var result = Run(
            quickstart,
            "DigitalBrain.Quickstart.Console",
            "net8.0",
            environment: DevelopmentEnvironment(quickstart),
            "--command-contract");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "role:reasoning",
                "conversation:generated-1",
                "conversation:conversation-1",
                "commands:/role /new /conversation /help /exit",
                "exit"
            ],
            ContractLines(result.Output));
    }

    [Theory]
    [InlineData("DigitalBrain.Quickstart.OrleansDashboard")]
    [InlineData("DigitalBrain.Quickstart.DevUI")]
    public void Development_tools_are_disabled_outside_development_by_default(
        string projectName)
    {
        var quickstart = fixture.PrepareQuickstart();
        var development = Run(
            quickstart,
            projectName,
            "net8.0",
            DevelopmentEnvironment(quickstart),
            "--startup-contract");
        Assert.True(development.ExitCode == 0, development.Output);
        Assert.Contains("development-host:ok", development.Output, StringComparison.Ordinal);

        var productionEnvironment = new Dictionary<string, string>(
            quickstart.Environment,
            StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DigitalBrain__DevTools__Owner"] = "quickstart-owner"
        };
        var production = Run(
            quickstart,
            projectName,
            "net8.0",
            productionEnvironment,
            "--startup-contract");
        Assert.NotEqual(0, production.ExitCode);
        Assert.Contains(
            "disabled outside Development",
            production.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppHost_model_contains_the_complete_quickstart_graph()
    {
        var quickstart = fixture.PrepareQuickstart();
        var result = Run(
            quickstart,
            "DigitalBrain.Quickstart.AppHost",
            "net11.0",
            DevelopmentEnvironment(quickstart),
            "--model-contract");

        Assert.Equal(0, result.ExitCode);
        var resources = ContractLines(result.Output)
            .Select(line => line["resource:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
                 {
                     "brain",
                     "kernel",
                     "console",
                     "orleans-dashboard",
                     "devui",
                     "brain-orleans",
                     "brain-orleans-client",
                     "brain-discovery-storage",
                     "brain-storage",
                     "brain-gptfast",
                     "brain-gptreasoning",
                     "brain-textembedding",
                     "brain-anthropic"
                 })
            Assert.Contains(expected, resources);
        Assert.DoesNotContain("quickstart-compose", resources);
    }

    [Fact]
    public void Live_AppHost_model_adds_controlled_provider_and_console_driver()
    {
        var quickstart = fixture.PrepareQuickstart();
        var environment = new Dictionary<string, string>(
            DevelopmentEnvironment(quickstart),
            StringComparer.Ordinal)
        {
            ["DigitalBrain__Quickstart__Live"] = "true",
            ["DigitalBrain__Quickstart__ProviderEndpoint"] =
                "http://127.0.0.1:5188",
            ["DigitalBrain__Quickstart__ProviderPort"] = "5188",
            ["DigitalBrain__Quickstart__DriverPort"] = "5189"
        };
        var result = Run(
            quickstart,
            "DigitalBrain.Quickstart.AppHost",
            "net11.0",
            environment,
            "--model-contract");

        Assert.Equal(0, result.ExitCode);
        var contract = ContractLines(result.Output);
        var resources = contract
            .Select(line => line["resource:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("test-provider", resources);
        Assert.Contains("console-test-driver", resources);
        Assert.Contains(
            "endpoint:openai:http://127.0.0.1:5188/v1",
            contract);
        Assert.Contains(
            "endpoint:anthropic:http://127.0.0.1:5188/",
            contract);
    }

    [Fact]
    public void Live_quickstart_uses_normal_clients_and_a_provider_neutral_test_resource()
    {
        var providerRoot = Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.TestProvider");
        var providerProject = Path.Combine(
            providerRoot,
            "DigitalBrain.Quickstart.TestProvider.csproj");
        Assert.True(File.Exists(providerProject), providerProject);
        var references = XDocument
            .Load(providerProject)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.DoesNotContain(
            references,
            reference =>
                reference.StartsWith("DigitalBrain", StringComparison.Ordinal) ||
                reference.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));

        var appHost = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.AppHost",
            "AppHost.cs"));
        Assert.Contains("ControlledGptFast", appHost, StringComparison.Ordinal);
        Assert.Contains("ControlledClaudeBalanced", appHost, StringComparison.Ordinal);
        Assert.Contains("ControlledGptReasoning", appHost, StringComparison.Ordinal);
        Assert.Contains("ControlledTextEmbedding", appHost, StringComparison.Ordinal);
        Assert.Contains("test-provider", appHost, StringComparison.Ordinal);
        Assert.Contains("console-test-driver", appHost, StringComparison.Ordinal);
        Assert.Contains(
            "DigitalBrain__Quickstart__OpenAISecret",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "DigitalBrain__Quickstart__AnthropicSecret",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains("ParameterResource", appHost, StringComparison.Ordinal);
        Assert.Contains(
            "brain-openai-openai-apikey",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "brain-anthropic-api-key",
            appHost,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IChatClient", appHost, StringComparison.Ordinal);

        var driver = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.Console",
            "QuickstartLiveDriver.cs"));
        Assert.Contains("DigitalBrainSessionFactory", driver, StringComparison.Ordinal);
        Assert.Contains("SubmitTurnAsync", driver, StringComparison.Ordinal);
        Assert.Contains("ReadAsync", driver, StringComparison.Ordinal);
        Assert.Contains("UseOtlpExporter", driver, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.TryParse", driver, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "request.Role",
            driver,
            StringComparison.Ordinal);

        var provider = File.ReadAllText(Path.Combine(
            providerRoot,
            "Program.cs"));
        Assert.Contains(
            "DigitalBrain:Quickstart:OpenAISecret",
            provider,
            StringComparison.Ordinal);
        Assert.Contains(
            "DigitalBrain:Quickstart:AnthropicSecret",
            provider,
            StringComparison.Ordinal);

        var kernel = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.Kernel",
            "Program.cs"));
        Assert.Contains("UseOtlpExporter", kernel, StringComparison.Ordinal);

        foreach (var project in new[]
                 {
                     "DigitalBrain.Quickstart.Console",
                     "DigitalBrain.Quickstart.Kernel"
                 })
        {
            var projectSource = File.ReadAllText(Path.Combine(
                QuickstartRoot(),
                project,
                $"{project}.csproj"));
            Assert.Contains(
                "OpenTelemetry.Exporter.OpenTelemetryProtocol",
                projectSource,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Live_gate_covers_recovery_devtools_telemetry_and_teardown()
    {
        var liveGatePath = Path.Combine(
            QuickstartRoot(),
            "Test-LiveQuickstart.ps1");
        Assert.True(File.Exists(liveGatePath), liveGatePath);
        var liveGate = File.ReadAllText(liveGatePath);

        foreach (var required in new[]
                 {
                     "aspire wait test-provider",
                     "aspire wait brain-storage",
                     "aspire wait kernel",
                     "aspire wait console-test-driver",
                     "aspire wait orleans-dashboard",
                     "aspire wait devui",
                     "NUGET_PACKAGES",
                     "dotnet restore",
                     "--force-evaluate",
                     "dotnet build",
                     "--no-incremental",
                     "aspire start",
                     "--no-build",
                     "aspire resource kernel restart",
                     "/dashboard",
                     "/v1/entities",
                     "/v1/responses",
                     "aspire describe",
                     "aspire logs kernel",
                     "aspire otel traces kernel",
                     "gen_ai.operation.name",
                     "chat claude-sonnet-4-5",
                     "digitalbrain.conversation.submit",
                     "aspire stop",
                     "aspire ps"
                 })
            Assert.Contains(required, liveGate, StringComparison.Ordinal);

        foreach (var redactionRequirement in new[]
                 {
                     "Get-RedactedUri",
                     "$builder.Query = [string]::Empty",
                     "$builder.Fragment = [string]::Empty",
                     "$builder.UserName = [string]::Empty",
                     "$builder.Password = [string]::Empty"
                 })
            Assert.Contains(
                redactionRequirement,
                liveGate,
                StringComparison.Ordinal);

        var oldKernelExit = liveGate.IndexOf(
            "Assert-ProcessStopped $kernelProcessId",
            StringComparison.Ordinal);
        var recoveryRead = liveGate.IndexOf(
            "$snapshot = Invoke-JsonGetWithRetry $snapshotUri",
            StringComparison.Ordinal);
        Assert.InRange(oldKernelExit, 0, recoveryRead - 1);

        var entryGate = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "eng",
            "test-quickstart.ps1"));
        Assert.Contains(
            "Test-LiveQuickstart.ps1",
            entryGate,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Quickstart_runner_keeps_the_console_interactive_and_local()
    {
        var appHostSource = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.AppHost",
            "AppHost.cs"));
        Assert.Contains(".WithExplicitStart()", appHostSource, StringComparison.Ordinal);
        Assert.Contains(
            @".WithArgs(""--environment-probe"")",
            appHostSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddDockerComposeEnvironment",
            appHostSource,
            StringComparison.Ordinal);

        var consoleSource = File.ReadAllText(Path.Combine(
            QuickstartRoot(),
            "DigitalBrain.Quickstart.Console",
            "Program.cs"));
        Assert.Contains("--environment-probe", consoleSource, StringComparison.Ordinal);
        Assert.Contains("host.RunAsync", consoleSource, StringComparison.Ordinal);

        var runnerPath = Path.Combine(QuickstartRoot(), "Start-Quickstart.ps1");
        Assert.True(File.Exists(runnerPath), runnerPath);
        var runner = File.ReadAllText(runnerPath);
        Assert.Contains("$AspireCommand start", runner, StringComparison.Ordinal);
        Assert.Contains(
            "$AspireCommand resource console start",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "$AspireCommand describe console",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "$AspireCommand resource console stop",
            runner,
            StringComparison.Ordinal);
        Assert.Contains("$DotnetCommand run", runner, StringComparison.Ordinal);
        Assert.Contains("$AspireCommand stop", runner, StringComparison.Ordinal);

        var readme = File.ReadAllText(Path.Combine(QuickstartRoot(), "README.md"));
        Assert.Contains(@".\eng\pack.ps1", readme, StringComparison.Ordinal);
        Assert.Contains(
            @".\samples\DigitalBrain.Quickstart\Start-Quickstart.ps1",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("Development-only", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Quickstart_runner_preserves_lifecycle_contract(
        bool failStart,
        bool failStop,
        bool failCleanupProbe)
    {
        var runnerPath = Path.Combine(QuickstartRoot(), "Start-Quickstart.ps1");
        var runnerSource = File.ReadAllText(runnerPath);
        Assert.Contains("[string]$AspireCommand", runnerSource, StringComparison.Ordinal);
        Assert.Contains("[string]$DotnetCommand", runnerSource, StringComparison.Ordinal);

        var contractRoot = Path.Combine(
            Path.GetTempPath(),
            $"digitalbrain-quickstart-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contractRoot);
        try
        {
            var aspireShim = Path.Combine(contractRoot, "aspire-shim.ps1");
            var dotnetShim = Path.Combine(contractRoot, "dotnet-shim.ps1");
            var wrapper = Path.Combine(contractRoot, "invoke.ps1");
            var log = Path.Combine(contractRoot, "commands.log");
            var marker = Path.Combine(contractRoot, "running");

            await File.WriteAllTextAsync(
                aspireShim,
                """
                $command = $args[0]
                $logEntry = if ($command -eq 'wait') {
                    "wait:$($args[1])"
                }
                elseif ($command -eq 'resource') {
                    "resource:$($args[2])"
                }
                else {
                    $command
                }
                Add-Content `
                    -LiteralPath $env:QUICKSTART_SHIM_LOG `
                    -Value $logEntry
                if ($command -eq 'ps') {
                    $probeCount = @(
                        Get-Content `
                            -LiteralPath $env:QUICKSTART_SHIM_LOG |
                            Where-Object { $_ -eq 'ps' }).Count
                    if (
                        $probeCount -gt 1 -and
                        $env:QUICKSTART_FAIL_CLEANUP_PROBE -eq '1') {
                        exit 5
                    }
                    if (Test-Path -LiteralPath $env:QUICKSTART_SHIM_RUNNING) {
                        [pscustomobject]@{
                            appHostPath = $env:QUICKSTART_APPHOST_PATH
                        } | ConvertTo-Json -Compress
                    }
                    else {
                        '[]'
                    }
                    exit 0
                }
                if ($command -eq 'start') {
                    [IO.File]::WriteAllText(
                        $env:QUICKSTART_SHIM_RUNNING,
                        'running')
                    Write-Output 'provider-prompt-visible'
                    if ($env:QUICKSTART_FAIL_START -eq '1') {
                        exit 2
                    }
                    exit 0
                }
                if ($command -eq 'describe') {
                    [pscustomobject]@{
                        resources = @(
                            [pscustomobject]@{
                                environment = [pscustomobject]@{
                                    DigitalBrain__DevTools__Owner = 'shim-owner'
                                    DOTNET_ENVIRONMENT = 'Development'
                                }
                            })
                    } | ConvertTo-Json -Depth 5 -Compress
                    exit 0
                }
                if ($command -eq 'stop') {
                    if ($env:QUICKSTART_FAIL_STOP -eq '1') {
                        exit 4
                    }
                    Remove-Item -LiteralPath $env:QUICKSTART_SHIM_RUNNING -Force
                    exit 0
                }
                exit 0
                """);
            await File.WriteAllTextAsync(
                dotnetShim,
                """
                Add-Content -LiteralPath $env:QUICKSTART_SHIM_LOG -Value (
                    "dotnet:$env:DigitalBrain__DevTools__Owner" +
                    ":$env:DOTNET_ENVIRONMENT")
                exit 0
                """);
            await File.WriteAllTextAsync(
                wrapper,
                """
                $env:DOTNET_ENVIRONMENT = 'Caller'
                try {
                    & $env:QUICKSTART_RUNNER_PATH `
                        -AspireCommand $env:QUICKSTART_ASPIRE_COMMAND `
                        -DotnetCommand $env:QUICKSTART_DOTNET_COMMAND
                    Write-Output 'completed'
                }
                catch {
                    Write-Output "caught:$($_.Exception.Message)"
                }
                Write-Output "restored:$env:DOTNET_ENVIRONMENT"
                """);

            var appHostPath = Path.GetFullPath(Path.Combine(
                QuickstartRoot(),
                "DigitalBrain.Quickstart.AppHost",
                "DigitalBrain.Quickstart.AppHost.csproj"));
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["QUICKSTART_RUNNER_PATH"] = runnerPath,
                ["QUICKSTART_ASPIRE_COMMAND"] = aspireShim,
                ["QUICKSTART_DOTNET_COMMAND"] = dotnetShim,
                ["QUICKSTART_SHIM_LOG"] = log,
                ["QUICKSTART_SHIM_RUNNING"] = marker,
                ["QUICKSTART_APPHOST_PATH"] = appHostPath,
                ["QUICKSTART_FAIL_START"] = failStart ? "1" : "0",
                ["QUICKSTART_FAIL_STOP"] = failStop ? "1" : "0",
                ["QUICKSTART_FAIL_CLEANUP_PROBE"] =
                    failCleanupProbe ? "1" : "0"
            };
            var result = await RunPowerShellAsync(contractRoot, environment, wrapper);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "provider-prompt-visible",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains("restored:Caller", result.Output, StringComparison.Ordinal);
            Assert.Equal(
                failStart
                    ? ["ps", "start", "ps", "stop"]
                    :
                    [
                        "ps",
                        "start",
                        "wait:kernel",
                        "resource:start",
                        "wait:console",
                        "describe",
                        "resource:stop",
                        "dotnet:shim-owner:Development",
                        "ps",
                        "stop"
                    ],
                File.ReadAllLines(log));

            if (failStart)
                Assert.Contains(
                    "aspire start failed with exit code 2.",
                    result.Output,
                    StringComparison.Ordinal);

            if (failStop)
            {
                Assert.Contains(
                    "aspire stop failed with exit code 4.",
                    result.Output,
                    StringComparison.Ordinal);
                Assert.True(File.Exists(marker));
            }
            else
                Assert.False(File.Exists(marker));

            if (failCleanupProbe)
                Assert.Contains(
                    "aspire ps failed with exit code 5.",
                    result.Output,
                    StringComparison.Ordinal);

            var caughtLines = result.Output
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("caught:", StringComparison.Ordinal))
                .ToArray();
            if (failStart || failStop || failCleanupProbe)
            {
                var caught = Assert.Single(caughtLines);
                Assert.DoesNotContain(
                    "completed",
                    result.Output,
                    StringComparison.Ordinal);
                if (failStart && failStop)
                {
                    var operationIndex = caught.IndexOf(
                        "aspire start failed",
                        StringComparison.Ordinal);
                    var cleanupIndex = caught.IndexOf(
                        "aspire stop failed",
                        StringComparison.Ordinal);
                    Assert.True(operationIndex >= 0, caught);
                    Assert.True(cleanupIndex > operationIndex, caught);
                }
            }
            else
            {
                Assert.Empty(caughtLines);
                Assert.Contains("completed", result.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(contractRoot, true);
        }
    }

    private string QuickstartRoot() => Path.Combine(
        fixture.RepositoryRoot,
        "samples",
        "DigitalBrain.Quickstart");

    private static IReadOnlyDictionary<string, string> DevelopmentEnvironment(
        QuickstartBuild quickstart) =>
        new Dictionary<string, string>(quickstart.Environment, StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DigitalBrain__DevTools__Owner"] = "quickstart-owner",
            ["DigitalBrain__Client__Name"] = "brain",
            ["DigitalBrain__Client__ContractVersion"] = "1",
            ["Orleans__ClusterId"] = "brain-cluster",
            ["Orleans__ServiceId"] = "brain-service",
            ["Orleans__Clustering__ProviderType"] = "AzureTableStorage",
            ["Orleans__Clustering__ServiceKey"] = "brain-clustering",
            ["ConnectionStrings__brain-clustering"] =
                "http://127.0.0.1:10002/devstoreaccount1"
        };

    private static (int ExitCode, string Output) Run(
        QuickstartBuild quickstart,
        string projectName,
        string targetFramework,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments)
    {
        var assembly = quickstart.Assembly(projectName, targetFramework);
        Assert.True(File.Exists(assembly), assembly);
        return DotnetCli.Run(
            quickstart.Root,
            environment,
            TimeSpan.FromSeconds(30),
            [assembly, .. arguments]);
    }

    private static string[] ContractLines(string output) =>
        output
            .Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                line.StartsWith("role:", StringComparison.Ordinal) ||
                line.StartsWith("conversation:", StringComparison.Ordinal) ||
                line.StartsWith("commands:", StringComparison.Ordinal) ||
                line == "exit" ||
                line.StartsWith("resource:", StringComparison.Ordinal) ||
                line.StartsWith("endpoint:", StringComparison.Ordinal))
            .ToArray();

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string script)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        foreach (var (name, value) in environment)
            process.StartInfo.Environment[name] = value;

        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("The quickstart runner contract timed out.");
        }

        return (
            process.ExitCode,
            string.Concat(
                await standardOutput,
                Environment.NewLine,
                await standardError));
    }

    private static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
               !relative.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal);
    }
}
