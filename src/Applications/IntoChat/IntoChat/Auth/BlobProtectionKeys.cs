using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Azure;
using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace IntoChat;

// Each key is an immutable blob, so concurrent hosts can append keys without a shared lock.
internal sealed class BlobProtectionKeys([FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient blobs, IHostEnvironment environment, IConfiguration configuration) : IXmlRepository
{
    private BlobContainerClient Container()
    {
        var container = blobs.GetBlobContainerClient("intochat-protection-v1");
        container.CreateIfNotExists();
        return container;
    }
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var container = Container();
        var elements = container.GetBlobs().Select(blob => XElement.Parse(container.GetBlobClient(blob.Name).DownloadContent().Value.Content.ToString())).ToArray();
        if (!environment.IsDevelopment() && !configuration.GetValue<bool>("DigitalBrain:Testing:Enabled"))
        { ValidateProductionRing(elements); }
        return elements;
    }
    internal static void ValidateProductionRing(IEnumerable<XElement> elements)
    {
        foreach (var key in elements.Where(element => element.Name.LocalName == "key"))
        {
            if (!key.Descendants().Attributes("decryptorType").Any(attribute => attribute.Value.StartsWith(
                typeof(EncryptedXmlDecryptor).FullName + ",", StringComparison.Ordinal)))
            { throw new InvalidOperationException("The shared key ring contains development or machine-bound keys. Migrate it to certificate-encrypted keys before using it in production."); }
        }
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        var content = BinaryData.FromString(element.ToString(SaveOptions.DisableFormatting));
        var name = Convert.ToHexStringLower(SHA256.HashData(content.ToArray())) + ".xml";
        try { Container().GetBlobClient(name).Upload(content, overwrite: false); }
        catch (RequestFailedException e) when (e.Status == 409) { /* Identical immutable key already exists. */ }
    }
}

internal static class BlobProtectionRegistration
{
    public static void AddDurableProtection(this WebApplicationBuilder builder)
    {
        var protection = builder.Services.AddDataProtection().SetApplicationName("IntoChat.v1");
        builder.Services.AddSingleton<BlobProtectionKeys>();
        builder.Services.AddOptions<KeyManagementOptions>().Configure<BlobProtectionKeys>((options, repository) => options.XmlRepository = repository);
        if (builder.Configuration["IntoChat:DataProtection:Certificate"] is { Length: > 0 } certificate)
        {
            var active = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(certificate),
                builder.Configuration["IntoChat:DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            var previous = builder.Configuration.GetSection("IntoChat:DataProtection:PreviousCertificates").Get<ProtectionCertificate[]>() ?? [];
            protection.ProtectKeysWithCertificate(active).UnprotectKeysWithAnyCertificate([active, .. previous.Select(item =>
                X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(item.Pfx), item.Password, X509KeyStorageFlags.EphemeralKeySet))]);
        }
        else if (builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled"))
        {
            // Azurite development key ring has the same storage trust boundary as the state.
            // Explicitly avoid default Windows DPAPI, which binds recovery to a local profile.
            builder.Services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new NullXmlEncryptor());
        }
        else
        {
            throw new InvalidOperationException("Configure IntoChat:DataProtection:Certificate (base64 PFX) for a portable, encrypted key ring.");
        }
        builder.Services.AddSingleton<IKeyWrapper, DataProtectionKeyWrapper>();
    }
    private sealed record ProtectionCertificate(string Pfx, string? Password);
}
