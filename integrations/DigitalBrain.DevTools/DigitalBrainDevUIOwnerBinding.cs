namespace DigitalBrain.DevTools;

internal sealed class DigitalBrainDevUIOwnerBinding(
    DigitalBrainSessionFactory sessionFactory,
    BrainOwnerId owner)
{
    private readonly object _gate = new();
    private int _validated;

    public bool IsValidated => Volatile.Read(ref _validated) != 0;

    public DigitalBrainConversationChatClient CreateClient(ConversationRole role)
    {
        EnsureValidated();
        return new DigitalBrainConversationChatClient(sessionFactory, owner, role);
    }

    private void EnsureValidated()
    {
        if (IsValidated)
            return;

        lock (_gate)
        {
            if (IsValidated)
                return;

            var session = sessionFactory.Create(owner);
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Volatile.Write(ref _validated, 1);
        }
    }
}
