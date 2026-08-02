using System.Reflection;
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

    [Fact(DisplayName = "no packable hosting package bakes a local product URL; the AppHost owns the stable port")]
    public void ProductCompositionOwnsTheLocalCallbackSurface()
    {
        var constants = HostingPackageConstants().ToArray();
        Assert.NotEmpty(constants);

        var baked = constants
            .Where(constant => constant.Value.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            .Select(constant => $"{constant.Owner} = {constant.Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(baked);

        var productSurface = RepositoryFile("os", "DigitalBrain.OS.AppHost", "ProductSurfaceResources.cs");
        Assert.Contains(ProductCallbackUri, productSurface, StringComparison.Ordinal);
        Assert.Contains("UiHttpPort = 5080", productSurface, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "product AppHost declares UserSecretsId so dashboard can save parameter secrets")]
    public void ProductAppHostDeclaresUserSecretsId()
    {
        var text = RepositoryFile("os", "DigitalBrain.OS.AppHost", "DigitalBrain.OS.AppHost.csproj");
        Assert.Contains("<UserSecretsId>", text, StringComparison.Ordinal);
        Assert.Contains("digitalbrain-os-apphost-", text, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Owner, string Value)> HostingPackageConstants()
        => ShippedHostingAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (
                Owner: $"{field.DeclaringType!.FullName}.{field.Name}",
                Value: field.GetRawConstantValue() as string ?? string.Empty));

    private static IEnumerable<Assembly> ShippedHostingAssemblies()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(McpProviderParameterHonesty).Assembly]);

        while (pending.Count > 0)
        {
            foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
            {
                if (!visited.Add(reference.Name!))
                {
                    continue;
                }

                var assembly = Assembly.Load(reference);
                pending.Enqueue(assembly);

                if (reference.Name!.StartsWith("DigitalBrain.", StringComparison.Ordinal)
                    && reference.Name.Contains(".Aspire.Hosting", StringComparison.Ordinal))
                {
                    yield return assembly;
                }
            }
        }
    }

    private static string RepositoryFile(params string[] segments)
    {
        string[] climbs = ["..\\..\\..\\..\\..", "..\\..\\..\\..\\..\\.."];
        foreach (var climb in climbs)
        {
            var candidate = Path.GetFullPath(
                Path.Combine([AppContext.BaseDirectory, climb, .. segments]));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            $"'{string.Join('/', segments)}' was not found above test base '{AppContext.BaseDirectory}'.");
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
