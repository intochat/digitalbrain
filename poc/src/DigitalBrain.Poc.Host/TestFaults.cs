namespace DigitalBrain.Poc.Host;

public enum HostFault
{
    None,
    PauseAfterIngressClosedBeforeDrain,
    BeforeCandidateChildReady,
    AfterPointerAdvanceBeforeActivation,
    AfterAuthorityReleaseBeforeAcknowledgement,
    ForceActivationRecoveryFailure,
    PauseBeforeOldRunRetirement,
    AfterTrustedFanOutCommitBeforeRuleAcknowledgement,
    AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement,
    AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement,
}
