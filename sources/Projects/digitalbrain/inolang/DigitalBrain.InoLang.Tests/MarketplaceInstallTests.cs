using System.IO.Compression;
using System.Reflection;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Kernel.Runtime.Neurons;
using DigitalBrain.Runtime.Marketplace;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

// MKT-3: a published free bundle installs end-to-end through the marketplace
// (download -> repack -> LocalBundleInstaller -> compile -> register), and a
// premium bundle is refused without a license.
public class MarketplaceInstallTests
{
    private const string FreeNeuronIno =
        "neuron Marketplace.TestInstalled\n" +
        "  using ask = synapse(Marketplace.TestReq)\n" +
        "  using ready = synapse(Marketplace.TestReady)\n" +
        "  on ask:\n" +
        "    emit ready(result: \"Installed\")\n";

    private static string Manifest(string bundleId, string version) =>
        $"{{\"bundleId\":\"{bundleId}\",\"version\":\"{version}\"," +
        "\"neurons\":[{\"fqn\":\"Marketplace.TestInstalled\",\"sourcePath\":\"neuron.ino\"}]}";

    private static byte[] BuildBundleZip(string manifestJson, string ino)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                w.Write(manifestJson);
            }
            using (var w = new StreamWriter(archive.CreateEntry("neuron.ino").Open()))
            {
                w.Write(ino);
            }
        }
        return ms.ToArray();
    }

    // The publish + install compile path binds against the real contract catalog
    // (strict), so the bundle's declared synapses must be known before compilation.
    private static void RegisterTestContracts(TestDigitalBrain brain)
    {
        var appField = typeof(TestDigitalBrain).GetField("_app", BindingFlags.NonPublic | BindingFlags.Instance);
        var app = (WebApplication)appField!.GetValue(brain)!;
        var catalog = app.Services.GetRequiredService<IContractCatalog>();
        catalog.Register(new ContractSchema("Marketplace.TestReq", ContractKind.Synapse, ["text"]));
        catalog.Register(new ContractSchema("Marketplace.TestReady", ContractKind.Synapse, ["result"]));
    }

    [Fact]
    public async Task FreeBundle_PublishThenInstall_RegistersNeuron()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            RegisterTestContracts(brain);

            const string bundleId = "test/free-install";
            const string version = "1.0.0";
            var manifestJson = Manifest(bundleId, version);
            var zip = BuildBundleZip(manifestJson, FreeNeuronIno);

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");

            var publish = await market.PublishBundleAsync(bundleId, version, manifestJson, zip);
            publish.Success.Should().BeTrue(string.Join("; ", publish.Diagnostics));

            var install = await market.InstallMarketplaceNeuronAsync(bundleId, "user-1");

            install.Success.Should().BeTrue(string.Join("; ", install.Diagnostics));
            install.Diagnostics.Should().Contain(d => d.Contains("Marketplace.TestInstalled"));
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task PremiumBundle_InstallWithoutLicense_IsDenied()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            const string bundleId = "test/premium-install";
            var zip = BuildBundleZip(Manifest(bundleId, "1.0.0"), FreeNeuronIno);

            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            await db.InsertBundleAsync(
                new BundleInfo(bundleId, "1.0.0", "{}", System.Array.Empty<byte>(), "29.99", "commercial", zip));

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");
            var install = await market.InstallMarketplaceNeuronAsync(bundleId, "user-without-license");

            install.Success.Should().BeFalse("a premium bundle must not install without a license");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }
}
