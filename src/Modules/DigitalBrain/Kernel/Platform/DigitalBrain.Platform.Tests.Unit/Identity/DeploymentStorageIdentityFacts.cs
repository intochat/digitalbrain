using DigitalBrain.Platform.Identity.Directory;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class DeploymentStorageIdentityFacts
{
    [Fact]
    public void ExistingStorageRequiresTheOriginalServiceAndMasterKey()
    {
        var keys = Wrapper("original-master-key");
        var binding = new DeploymentStorageIdentity.Binding(1, "stable-service",
            keys.Wrap("DigitalBrain deployment key binding v1"u8.ToArray()));
        DeploymentStorageIdentity.Validate(binding, "stable-service", keys);
        var changedService = Assert.Throws<OptionsValidationException>(() =>
            DeploymentStorageIdentity.Validate(binding, "different-service", keys));
        Assert.Equal("ServiceId", changedService.OptionsName);
        var changedKey = Assert.Throws<OptionsValidationException>(() =>
            DeploymentStorageIdentity.Validate(binding, "stable-service", Wrapper("replacement-master-key")));
        Assert.Equal("MasterKey", changedKey.OptionsName);
    }

    [Fact]
    public void UnknownBindingVersionsCannotBeSilentlyAdopted()
    {
        Assert.Throws<InvalidOperationException>(() => DeploymentStorageIdentity.Validate(
            new(2, "stable-service", "unknown-format"), "stable-service", Wrapper("original-master-key")));
    }

    private static MasterKeyWrapper Wrapper(string key) => new(Options.Create(new MasterKeyOptions { MasterKey = key }));
}
