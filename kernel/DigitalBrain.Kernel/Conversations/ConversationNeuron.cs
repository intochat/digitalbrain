using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

internal sealed class ConversationNeuron
    : Neuron, IConversationNeuron, IConversationGrain
{
    private readonly ConversationDurableState _conversationState;
    private readonly IConversationRoleInvoker _roleInvoker;
    private readonly ConversationActivationGuard _activationGuard = new();

    public ConversationNeuron(
        [NeuronState] NeuronDurableState state,
        [ConversationState] ConversationDurableState conversationState,
        IServiceProvider services)
        : base(state)
    {
        _conversationState = conversationState;
        _roleInvoker = services.GetRequiredService<IConversationRoleInvoker>();
    }

    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnRequest request) =>
        CreateCoordinator().SubmitTurnAsync(request);

    public Task<ConversationSnapshot> ReadAsync() =>
        Task.FromResult(CreateCoordinator().Read());

    private ConversationTurnCoordinator CreateCoordinator() =>
        new(
            DurableState,
            _conversationState,
            _roleInvoker,
            CommitDurableStateAsync,
            cancellationToken => DrainOutboxCoreAsync(
                throwOnPublishFailure: true,
                cancellationToken),
            () => NeuronReminder.RegisterOutboxRecoveryAsync(this),
            () => NeuronReminder.UnregisterOutboxRecoveryAsync(this),
            _activationGuard,
            DeactivateOnIdle);
}
