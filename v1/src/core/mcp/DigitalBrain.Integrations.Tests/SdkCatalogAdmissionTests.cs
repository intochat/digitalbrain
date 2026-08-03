using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class SdkCatalogAdmissionTests
{
    [Fact(DisplayName = "Allowlist admits exactly the five read-only Gmail tool names")]
    public void AllowlistAdmitsExactlyFiveReadOnlyTools()
    {
        Assert.Equal(
            [
                SdkCatalogAdmission.MessagesList,
                SdkCatalogAdmission.MessagesGet,
                SdkCatalogAdmission.ThreadsList,
                SdkCatalogAdmission.ThreadsGet,
                SdkCatalogAdmission.LabelsList,
            ],
            SdkCatalogAdmission.AllowedToolNames);
        Assert.Equal(5, SdkCatalogAdmission.AllowedSdkMembers.Count);
    }

    [Fact(DisplayName = "Structural walk of the SDK resource surface never admits mutating verbs")]
    public void SdkSurfaceMutatingVerbsAreNeverOnTheAllowlist()
    {
        var surface = SdkCatalogAdmission.EnumerateSdkResourceMethods();
        Assert.NotEmpty(surface);

        var mutating = surface
            .Where(member =>
            {
                var verb = member.Split('.').Last();
                return SdkCatalogAdmission.IsMutatingVerb(verb);
            })
            .ToArray();
        Assert.NotEmpty(mutating);

        foreach (var member in mutating)
        {
            Assert.DoesNotContain(
                SdkCatalogAdmission.AllowedSdkMembers,
                allowed => string.Equals(allowed, member, StringComparison.Ordinal)
                    || member.EndsWith(allowed.Split('.').Last(), StringComparison.Ordinal)
                       && allowed.Split('.')[^2] == member.Split('.')[^2]);
        }

        foreach (var allowed in SdkCatalogAdmission.AllowedSdkMembers)
        {
            var verb = allowed.Split('.').Last();
            Assert.False(SdkCatalogAdmission.IsMutatingVerb(verb), allowed);
        }
    }

    [Fact(DisplayName = "Built catalog tools have non-empty descriptions and hard-cap maxResults at 10")]
    public void BuiltCatalogHasDescriptionsAndMaxResultsCap()
    {
        using var service = new global::Google.Apis.Gmail.v1.GmailService(
            new global::Google.Apis.Services.BaseClientService.Initializer
            {
                ApplicationName = "DigitalBrain.Tests",
                ApiKey = "unused",
            });

        var catalog = SdkCatalogAdmission.Build(service);
        Assert.Equal(5, catalog.Count);
        Assert.All(catalog, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });

        Assert.Equal(10, SdkCatalogAdmission.BoundMaxResults(null));
        Assert.Equal(10, SdkCatalogAdmission.BoundMaxResults(0));
        Assert.Equal(10, SdkCatalogAdmission.BoundMaxResults(100));
        Assert.Equal(3, SdkCatalogAdmission.BoundMaxResults(3));
        Assert.Equal(10, SdkCatalogAdmission.BoundMaxResults(10));
    }
}
