using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Filters;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Onboarding;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding.Onboarding;

// Pure decision logic for the Onboarding gate. No Orleans/grain context, so it
// is unit-testable without booting Aspire. The thin OnboardingNeuron grain owns
// durable bookkeeping + FireSynapseAsync; this owns the synapse shapes and the
// fixed Terms RFW document.
public static class OnboardingPlan
{
    public const string CardLibrary = "digitalbrain";
    public const string CardRootWidget = "OnboardingCard";

    // Self-contained RFW document rendered under the digitalbrain dictionary. Bare
    // widget names, import digitalbrain; present, and the only event raised is
    // acceptPolicy (a host capability wired by Task 6).
    // LF-only: the RFW parser rejects U+000D (CR). Raw string literals on
    // Windows embed CRLF from the source file, so we normalize at definition time.
    public static readonly string TermsCardSource =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  padding: 20.0,\n" +
        "  child: VStack(\n" +
        "    gap: 14.0,\n" +
        "    cross: \"start\",\n" +
        "    children: [\n" +
        "      Text(text: data.termsText, variant: \"body\"),\n" +
        "      Button(label: \"Start\", onTap: event \"acceptPolicy\" { version: data.version }),\n" +
        "    ],\n" +
        "  ),\n" +
        ");\n";

    public static bool NeedsAccept(string? acceptedVersion, string currentVersion) =>
        acceptedVersion != currentVersion;

    public static string TermsCardDataJson(
        string termsText = OnboardingPolicy.DefaultTermsText,
        string version = OnboardingPolicy.DefaultTermsVersion) =>
        JsonSerializer.Serialize(new
        {
            termsText = termsText,
            version = version,
            source = TermsCardSource,
        });

    public static OnboardingResult ToResult(RequestOnboarding req, bool needsAccept, string version) =>
        new(NeedsAccept:        needsAccept,
        CurrentVersion:     version) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? CallerStampingOutgoingFilter.ExternalCallerSentinel,
            timestamp: default
        ) };

    public static RfwCard ToTermsCard(RequestOnboarding req, string termsText, string version) =>
        new(LibraryName:        CardLibrary,
        RootWidget:         CardRootWidget,
        DataJson:           TermsCardDataJson(termsText, version)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: default
        ) };

    public static PolicyAccepted ToPolicyAccepted(AcceptPolicy ap) =>
        new(UserId: ap.UserId, Version: ap.Version);
}
