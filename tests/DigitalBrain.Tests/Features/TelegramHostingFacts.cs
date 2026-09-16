using DigitalBrain.Telegram.Aspire.Hosting;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Telegram;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramHostingFacts
{
    [Theory]
    [InlineData(TelegramTunnelMode.Quick)]
    [InlineData(TelegramTunnelMode.Named)]
    public void Module_group_owns_build_and_tunnel_without_reparenting_the_shared_kernel(TelegramTunnelMode mode)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("brain").AddModule<TelegramModule>(module => module.WithBot(options =>
        {
            options.TunnelMode = mode;
            options.PublicUrl = mode == TelegramTunnelMode.Named ? "https://brain.example.test" : null;
            options.CloudflaredCommand = "cloudflared";
        }));
        var kernel = builder.AddExecutable("kernel", "dotnet", Environment.CurrentDirectory).WithReference(brain);
        var group = Assert.Single(builder.Resources.OfType<DigitalBrainModuleResource>());
        Assert.Same(group, brain.GetModuleResource<TelegramModule>().Resource);
        Assert.Equal(typeof(TelegramModule), group.ModuleType);
        Assert.Equal("telegram", group.Name);
        Assert.Equal("modules", brain.Resource.Resource.Name);
        Assert.Contains(group.Annotations.OfType<ResourceRelationshipAnnotation>(), relation => relation.Type == "Parent" && relation.Resource == brain.Resource.Resource);
        foreach (var name in new[] { "telegram-tunnel", "telegram-miniapp-build" })
        {
            var child = Assert.Single(builder.Resources, resource => resource.Name == name);
            Assert.Contains(child.Annotations.OfType<ResourceRelationshipAnnotation>(), relation => relation.Type == "Parent" && relation.Resource == group);
        }
        Assert.DoesNotContain(kernel.Resource.Annotations.OfType<ResourceRelationshipAnnotation>(), relation => relation.Resource == group);
        var storage = Assert.Single(builder.Resources, resource => resource.Name == "storage");
        Assert.Contains(storage.Annotations.OfType<ResourceRelationshipAnnotation>(), relation => relation.Type == "Parent" && relation.Resource == kernel.Resource);
    }

    [Fact]
    public void Named_tunnel_has_stable_origin_port_and_secret_parameter()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("brain").AddModule<TelegramModule>(module => module.WithBot(options =>
        {
            options.TunnelMode = TelegramTunnelMode.Named;
            options.PublicUrl = "https://brain.example.test";
            options.PublicPort = 5181;
            options.CloudflaredCommand = "cloudflared";
        }));
        var kernel = builder.AddExecutable("kernel", "dotnet", Environment.CurrentDirectory).WithReference(brain);
        var endpoint = Assert.Single(kernel.Resource.Annotations.OfType<EndpointAnnotation>(), item => item.Name == "telegram");
        Assert.Equal(5181, endpoint.Port);
        Assert.Equal(5181, endpoint.TargetPort);
        Assert.False(endpoint.IsProxied);
        var token = Assert.Single(builder.Resources.OfType<ParameterResource>(), item => item.Name == "cloudflare-tunnel-token");
        Assert.True(token.Secret);
        Assert.Single(builder.Resources.OfType<ExecutableResource>(), item => item.Name == "telegram-tunnel");
    }

    [Theory]
    [InlineData(null, 5181)]
    [InlineData("https://brain.example.test", 0)]
    [InlineData("http://brain.example.test", 5181)]
    public void Named_tunnel_invalid_configuration_never_falls_back_to_quick_tunnel(string? origin, int port)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var brain = builder.AddDigitalBrain("brain").AddModule<TelegramModule>(module => module.WithBot(options =>
        {
            options.TunnelMode = TelegramTunnelMode.Named;
            options.PublicUrl = origin;
            options.PublicPort = port;
        }));
        var error = Record.Exception(() => builder.AddExecutable("kernel", "dotnet", Environment.CurrentDirectory).WithReference(brain));
        Assert.True(error is ArgumentException or InvalidOperationException);
    }

    [Fact]
    public void Tunnel_origin_uses_latest_announcement_and_never_uses_localhost_or_deceptive_hosts()
    {
        Assert.Equal("https://new-public.trycloudflare.com", TelegramTunnelLog.ReadOrigin("https://old-public.trycloudflare.com\nhttps://new-public.trycloudflare.com |"));
        Assert.Null(TelegramTunnelLog.ReadOrigin("https://api.trycloudflare.com\nhttp://localhost:5080\nhttps://safe.trycloudflare.com.evil.test"));
        Assert.Equal("https://bot.example.test", TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://localhost:5080"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/private"));
    }

    [Fact]
    public async Task Missing_tunnel_log_times_out_instead_of_falling_back_to_localhost()
    {
        var log = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
        await Assert.ThrowsAsync<TimeoutException>(() => TelegramTunnelLog.WaitForOriginAsync(log, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken));
    }

}
