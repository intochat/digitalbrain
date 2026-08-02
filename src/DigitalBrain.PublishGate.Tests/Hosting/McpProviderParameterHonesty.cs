using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class McpProviderParameterHonesty
{
    private const string ProductCallbackUri = "http://localhost:5080/oauth/callback";

    private static readonly string[] ForbiddenCredentialPlaceholders =
    [
        "local-dev",
        "local-dev-secret",
        "http://localhost/oauth/callback",
    ];

    [Fact(DisplayName =
        "run-mode MCP credentials stay required while OAuth callback defaults to the product-supplied URL")]
    public async Task RunModeCredentialsRequiredAndCallbackDefaultsToProductSuppliedUrl()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.True(
            builder.ExecutionContext.IsRunMode,
            "CreateBuilder() must exercise the run-mode parameter path.");

        var brain = builder
            .AddDigitalBrain("brain")
            .WithLocalDevelopmentOAuthCallback(new Uri(ProductCallbackUri));
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var parameters = builder.Resources
            .OfType<ParameterResource>()
            .ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

        foreach (var name in new[]
                 {
                     "google-client-id",
                     "google-client-secret",
                     "salesforce-client-id",
                 })
        {
            Assert.True(parameters.ContainsKey(name), $"Expected parameter '{name}' to be registered.");
            await AssertCredentialHasNoFakeDefault(parameters[name]).ConfigureAwait(true);
        }

        Assert.False(
            parameters.ContainsKey("salesforce-client-secret"),
            "Salesforce must not register a client-secret parameter.");

        Assert.True(parameters.ContainsKey("google-redirect-uri"));
        Assert.True(parameters.ContainsKey("salesforce-redirect-uri"));
        Assert.Equal(
            ProductCallbackUri,
            await ResolveDefaultOrValue(parameters["google-redirect-uri"]).ConfigureAwait(true));
        Assert.Equal(
            ProductCallbackUri,
            await ResolveDefaultOrValue(parameters["salesforce-redirect-uri"]).ConfigureAwait(true));

        Assert.False(
            parameters.ContainsKey("mcp-authorization-mode"),
            "mcp-authorization-mode is deleted; sign-in is one flow.");

        // Aspire only enables dashboard user-secrets persistence when parameters are registered
        // with persist:true and the AppHost has a UserSecretsId (see DigitalBrain.OS.AppHost.csproj).
        Assert.NotNull(parameters["google-client-secret"].Default);
        Assert.NotNull(parameters["google-client-id"].Default);
    }

    [Fact(DisplayName = "without a product-supplied callback the run-mode redirect parameter has no default")]
    public async Task RedirectUriHasNoDefaultWhenTheProductSuppliesNoCallback()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<GoogleModule>(google => google.WithGmail());

        var redirect = Assert.Single(
            builder.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == "google-redirect-uri");

        Assert.Null(await ResolveDefaultOrValue(redirect).ConfigureAwait(true));
    }

    [Fact(DisplayName =
        "no source under a packable *.Aspire.Hosting package writes localhost or a product host-port literal, "
        + "whatever its accessibility or shape; a bare 5000 or 5080 there is a leak even if it was meant as a timeout")]
    public void ProductCompositionOwnsTheLocalCallbackSurface()
    {
        var productSurface = RepositoryFile("os", "DigitalBrain.OS.AppHost", "ProductSurfaceResources.cs");
        Assert.Contains(ProductCallbackUri, productSurface, StringComparison.Ordinal);
        Assert.Contains("UiHttpPort = 5080", productSurface, StringComparison.Ordinal);

        var productHostPorts = ProductHostPorts(productSurface);
        Assert.Equal(["5000", "5080"], productHostPorts);

        var sources = PackableHostingSources().ToArray();
        Assert.NotEmpty(sources);

        var leaks = sources
            .SelectMany(source => LeakingLines(source, productHostPorts))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaks);
    }

    [Fact(DisplayName = "product AppHost declares UserSecretsId so dashboard can save parameter secrets")]
    public void ProductAppHostDeclaresUserSecretsId()
    {
        var text = RepositoryFile("os", "DigitalBrain.OS.AppHost", "DigitalBrain.OS.AppHost.csproj");
        Assert.Contains("<UserSecretsId>", text, StringComparison.Ordinal);
        Assert.Contains("digitalbrain-os-apphost-", text, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ProductHostPorts(string productSurfaceSource)
        => [.. productSurfaceSource
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("public const int ", StringComparison.Ordinal)
                && line.Contains("Port = ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("Port = ", StringComparison.Ordinal) + 7)..].TrimEnd(';'))
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> PackableHostingSources()
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory
            .EnumerateDirectories(
                Path.Combine(RepositoryRoot(), "src"),
                "*.Aspire.Hosting",
                SearchOption.AllDirectories)
            .SelectMany(package => Directory.EnumerateFiles(package, "*.cs", SearchOption.AllDirectories))
            .Where(source => !source.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !source.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }

    private static IEnumerable<string> LeakingLines(string source, IReadOnlyList<string> productHostPorts)
    {
        var lines = File.ReadAllLines(source);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || productHostPorts.Any(port => WritesNumber(line, port)))
            {
                yield return $"{source}({index + 1}): {line.Trim()}";
            }
        }
    }

    private static bool WritesNumber(string line, string number)
    {
        for (var at = line.IndexOf(number, StringComparison.Ordinal);
            at >= 0;
            at = line.IndexOf(number, at + 1, StringComparison.Ordinal))
        {
            var end = at + number.Length;
            if ((at == 0 || !char.IsAsciiDigit(line[at - 1]))
                && (end == line.Length || !char.IsAsciiDigit(line[end])))
            {
                return true;
            }
        }

        return false;
    }

    private static string RepositoryFile(params string[] segments)
        => File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"No DigitalBrain.slnx above test base '{AppContext.BaseDirectory}'.");
    }

    private static async Task AssertCredentialHasNoFakeDefault(ParameterResource parameter)
    {
        if (parameter.Default is not null)
        {
            try
            {
                var defaultValue = parameter.Default.GetDefaultValue();
                foreach (var placeholder in ForbiddenCredentialPlaceholders)
                {
                    Assert.False(
                        string.Equals(defaultValue, placeholder, StringComparison.Ordinal),
                        $"Parameter '{parameter.Name}' must not default to placeholder '{placeholder}'.");
                }

                Assert.False(
                    string.Equals(defaultValue, ProductCallbackUri, StringComparison.Ordinal),
                    $"Credential parameter '{parameter.Name}' must not default to the OAuth callback URL.");
            }
            catch (MissingParameterValueException)
            {
                // Operator-supplied defaults fail closed until user secrets are set — expected.
            }
        }

        try
        {
            var resolved = await parameter.GetValueAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var placeholder in ForbiddenCredentialPlaceholders)
            {
                Assert.False(
                    string.Equals(resolved, placeholder, StringComparison.Ordinal),
                    $"Parameter '{parameter.Name}' must not resolve to placeholder '{placeholder}'.");
            }
        }
        catch (MissingParameterValueException)
        {
        }
    }

    private static async Task<string?> ResolveDefaultOrValue(ParameterResource parameter)
    {
        if (parameter.Default is not null)
        {
            try
            {
                return parameter.Default.GetDefaultValue();
            }
            catch (MissingParameterValueException)
            {
                return null;
            }
        }

        try
        {
            return await parameter.GetValueAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (MissingParameterValueException)
        {
            return null;
        }
    }
}
