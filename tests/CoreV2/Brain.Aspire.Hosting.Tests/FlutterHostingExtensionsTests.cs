using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.UI;
using Brain.Modules.UI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Brain.Aspire.Hosting.Tests;

public sealed class FlutterHostingExtensionsTests
{
    [Fact]
    public void Configured_host_defaults_to_headless_dart_without_a_window()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<UiModule>(ui => ui.WithConfiguredHost(builder.Configuration, options =>
        {
            options.WorkingDirectory = FindFlutterCore();
        }));

        var flutter = Assert.IsType<ExecutableResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "flutter"));
        Assert.Equal("dart", Path.GetFileNameWithoutExtension(flutter.Command), ignoreCase: true);
        Assert.Equal(FindFlutterCore(), flutter.WorkingDirectory);
    }

    [Fact]
    public void Configured_host_can_explicitly_opt_into_a_window()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ShellHostingExtensions.HostKindConfigurationKey] = "window",
        });
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<UiModule>(ui => ui.WithConfiguredHost(builder.Configuration, options =>
        {
            options.FlutterCommand = "flutter";
            options.WorkingDirectory = FindFlutterShell();
        }));

        var flutter = Assert.IsType<ExecutableResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "flutter"));
        Assert.Equal("flutter", flutter.Command);
        Assert.Equal(FindFlutterShell(), flutter.WorkingDirectory);
    }

    [Fact]
    public void Window_host_is_module_owned_and_waits_for_the_product_client()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<UiModule>(ui => ui.WithWindowHost(options =>
        {
            options.FlutterCommand = "flutter";
            options.WorkingDirectory = FindFlutterShell();
        }));

        var product = builder
            .AddExecutable("product", "dotnet", ".")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain.AsClient());

        var flutter = Assert.IsType<ExecutableResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "flutter"));
        Assert.Equal("flutter", flutter.Command);
        Assert.Equal(FindFlutterShell(), flutter.WorkingDirectory);
        Assert.NotEmpty(flutter.Annotations.OfType<CommandLineArgsCallbackAnnotation>());
        Assert.Contains(
            flutter.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource == product.Resource
                && annotation.WaitType == WaitType.WaitUntilHealthy);
        Assert.True(flutter.TryGetEnvironmentVariables(out var environment));
        Assert.NotEmpty(environment);
    }

    [Fact]
    public void A_brain_rejects_a_second_flutter_host()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<UiModule>(ui => ui.WithWindowHost(options =>
        {
            options.FlutterCommand = "flutter";
            options.WorkingDirectory = FindFlutterShell();
        }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            brain.AddModule<UiModule>(ui => ui.WithWebHost(options =>
            {
                options.FlutterCommand = "flutter";
                options.WorkingDirectory = FindFlutterShell();
            })));

        Assert.Contains("already configured", exception.Message, StringComparison.Ordinal);
    }

    private static string FindFlutterShell()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "CoreV2", "UI", "Flutter", "shell");
    }

    private static string FindFlutterCore()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "CoreV2", "UI", "Flutter", "core");
    }
}
