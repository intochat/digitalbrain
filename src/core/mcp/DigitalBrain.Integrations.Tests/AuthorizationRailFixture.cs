using System.Collections.Concurrent;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Salesforce;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Security;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationRailFixture : DigitalBrainFixture
{
    public const string PublicSignInBase = "https://ui.test.digitalbrain.local/";

    internal RecordingScramblingProtector CodeProtector { get; } = new();

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
        var codeProtector = CodeProtector;
        brain.ConfigureMcpEdge(services =>
        {
            services.RemoveAll<IDurablePayloadProtector>();
            services.AddSingleton<IDurablePayloadProtector>(codeProtector);
        });
        brain.Configure(McpRuntimeHosting.PublicSignInBaseKey, PublicSignInBase);
    }

    internal sealed class RecordingScramblingProtector : IDurablePayloadProtector
    {
        private readonly ConcurrentDictionary<string, byte[]> _protectedByPurpose =
            new(StringComparer.Ordinal);

        public bool TryGetProtected(string purpose, out byte[] protectedPayload)
            => _protectedByPurpose.TryGetValue(purpose, out protectedPayload!);

        public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            var protectedPayload = Scramble(plaintext);
            _protectedByPurpose[purpose] = protectedPayload;
            return protectedPayload;
        }

        public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedPayload)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            return Scramble(protectedPayload);
        }

        private static byte[] Scramble(ReadOnlySpan<byte> input)
        {
            var output = new byte[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                output[i] = (byte)(input[i] ^ 0xA5);
            }

            return output;
        }
    }
}
