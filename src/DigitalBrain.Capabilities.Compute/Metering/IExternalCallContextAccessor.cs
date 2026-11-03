using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Compute;

// Reads trusted ambient context at dispatch time. The returned ID is correlation only;
// ICostMeter independently captures the caller, brain, payer, and trace authority.
public interface IExternalCallContextAccessor
{
    string RequireTraceId();
}

// Custom primary transports must issue at most one upstream attempt per SendAsync.
// Put retries and bounded redirects in outer handlers so each dispatch is accounted for.
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface for single-attempt HTTP transports.")]
public interface ISingleAttemptHttpTransport;
