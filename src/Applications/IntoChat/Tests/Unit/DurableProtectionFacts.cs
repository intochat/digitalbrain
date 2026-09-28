using System.Xml.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DigitalBrain.Sdk.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.Tests.Unit;

public sealed class DurableProtectionFacts
{
    [Fact]
    public void CertificateRotationKeepsPastKeysReadableAndPlaintextRingIsRejected()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=IntoChat migration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var previous = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var nextRsa = RSA.Create(2048);
        using var next = new CertificateRequest("CN=IntoChat rotated test", nextRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var ring = new TestKeyRing();
        using var first = Host(ring, previous);
        var protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("keep");
        BlobProtectionKeys.ValidateProductionRing(ring.GetAllElements());
        using var rotated = Host(ring, next, previous);
        Assert.Equal("keep", rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedValue));
        var plain = new TestKeyRing();
        using var development = Host(plain);
        development.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("dev");
        Assert.Throws<InvalidOperationException>(() => BlobProtectionKeys.ValidateProductionRing(plain.GetAllElements()));
    }
    [Fact]
    public void FreshHostCanReadKeysAndLegacyOwnerKeyCanBeRewrapped()
    {
        var ring = new TestKeyRing();
        using var first = Host(ring);
        var firstKeys = new DataProtectionKeyWrapper(first.GetRequiredService<IDataProtectionProvider>());
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var legacy = "kv1:" + Convert.ToBase64String(key);
        Assert.True(DataProtectionKeyWrapper.NeedsMigration(legacy));
        var wrapped = firstKeys.Wrap(firstKeys.Unwrap(legacy));
        Assert.False(DataProtectionKeyWrapper.NeedsMigration(wrapped));
        Assert.DoesNotContain(Convert.ToBase64String(key), wrapped, StringComparison.Ordinal);
        using var second = Host(ring);
        Assert.Equal(key, new DataProtectionKeyWrapper(second.GetRequiredService<IDataProtectionProvider>()).Unwrap(wrapped));
        using var unrelated = Host(new TestKeyRing());
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            new DataProtectionKeyWrapper(unrelated.GetRequiredService<IDataProtectionProvider>()).Unwrap(wrapped));
    }

    private static ServiceProvider Host(IXmlRepository ring, X509Certificate2? active = null, params X509Certificate2[] previous)
    {
        var services = new ServiceCollection();
        var protection = services.AddDataProtection().SetApplicationName("IntoChat.v1");
        if (active is not null) { protection.ProtectKeysWithCertificate(active).UnprotectKeysWithAnyCertificate([active, .. previous]); }
        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = ring;
            if (active is null) { options.XmlEncryptor = new NullXmlEncryptor(); }
        });
        return services.BuildServiceProvider();
    }
    private sealed class TestKeyRing : IXmlRepository
    {
        private readonly List<XElement> _keys = [];
        public IReadOnlyCollection<XElement> GetAllElements() => _keys.Select(key => new XElement(key)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => _keys.Add(new XElement(element));
    }
}
