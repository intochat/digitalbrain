namespace DigitalBrain.Core;

internal sealed class NeuronActivationGuardFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron neuron)
        {
            // Drain and ReceiveReminder already handle the fence themselves.
            var declaringInterface = context.InterfaceMethod.DeclaringType;
            if (declaringInterface != typeof(INeuronInbox)
                && declaringInterface != typeof(IRemindable))
            {
                await neuron.GuardActivationAsync().ConfigureAwait(true);
            }
        }

        await context.Invoke().ConfigureAwait(true);
    }
}
