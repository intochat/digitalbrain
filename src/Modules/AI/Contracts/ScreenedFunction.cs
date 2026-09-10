using DigitalBrain.AI.Interactions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class ScreenedFunction(AIFunction tool, IUntrustedContentScreen screen) : DelegatingAIFunction(tool)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        var text = result as string ?? result?.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                await screen.ScreenAsync(text, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return error.Message;
            }
        }

        return result;
    }
}
