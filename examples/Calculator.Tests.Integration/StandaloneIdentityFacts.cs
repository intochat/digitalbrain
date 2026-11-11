using DigitalBrain;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Identity.Local;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Calculator.Tests.Integration;

public sealed class StandaloneIdentityFacts
{
    [Fact]
    public async Task AGrantSurvivesASiloRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        var home = Path.Combine(Path.GetTempPath(), "db-calc-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            Member owner;
            await using (var first = await StartHost(home, ct))
            {
                var identity = first.Services.GetRequiredService<IIdentity>();
                var grants = first.Services.GetRequiredService<IGrants>();
                owner = await LocalOwner.Ensure(identity, ct);

                using (RuntimeCallerContext.Stamp(new CapabilityCaller(owner.PrincipalId, owner.AccountId, owner.BrainId)))
                {
                    await grants.GrantAsOwner(
                        new Grant
                        {
                            AppId = "calculator",
                            SemanticTypeId = "calculator.history",
                            Mode = GrantMode.Always,
                            WorkspaceId = owner.BrainId,
                        },
                        ct);
                }
            }

            await using (var second = await StartHost(home, ct))
            {
                var grants = second.Services.GetRequiredService<IGrants>();
                using (RuntimeCallerContext.Stamp(new CapabilityCaller("app:calculator", owner.AccountId, owner.BrainId, "calculator")))
                {
                    var decision = await grants.Authorize(
                        new CallRequest
                        {
                            TargetNeuron = $"{owner.BrainId}/app.calculator/default",
                            Operation = "Record",
                            SemanticTypeIds = ["calculator.history"],
                        },
                        ct);
                    Assert.NotNull(decision);
                    Assert.True(decision.Allowed);
                }
            }
        }
        finally
        {
            if (Directory.Exists(home))
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { Directory.Delete(home, recursive: true); break; }
                    catch (IOException) when (attempt < 10) { await Task.Delay(100, CancellationToken.None); }
                    catch (UnauthorizedAccessException) when (attempt < 10) { await Task.Delay(100, CancellationToken.None); }
                }
            }
        }
    }

    private static Task<IInProcessHost> StartHost(string home, CancellationToken ct)
        => new LocalSiloHostFactory(null, services => services.AddLocalIdentity(SiloNeurons.Get, home))
            .Start(new InProcessHostOptions { Home = home, Modules = [typeof(CalculatorScreen)] }, ct);
}
