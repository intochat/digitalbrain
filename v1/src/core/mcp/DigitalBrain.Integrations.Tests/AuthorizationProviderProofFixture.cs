using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Salesforce;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationProviderProofFixture : DigitalBrainFixture
{
    private readonly ScriptedChatClient _plannerChat = new();

    internal ScriptedChatClient PlannerChat => _plannerChat;

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GoogleModule>();
        brain.AddModule<SalesforceModule>();
        brain.AddModule<EnrichmentModule>();
        brain.AddModule<ShellModule>();
        brain.AddModule<IntegrationsHarnessModule>();
        IntegrationsGmailHosts.ResetRuntimeState();
        IntegrationsGmailHosts.ApplyConfiguration(brain);
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            IntegrationsFixture.SampleMessageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody);
        brain.ConfigureServiceEdge(
            services => services.AddSingleton<IChatClient>(_plannerChat),
            _plannerChat,
            static chat => chat.Reset());
        brain.WithResponseTimeout(TimeSpan.FromMinutes(2));
    }
}
