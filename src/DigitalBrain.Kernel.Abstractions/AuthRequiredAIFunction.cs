namespace DigitalBrain.Kernel;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

// Generic auth gate for any AIFunction tool: checks connection state before invoking, never leaking
// the inner function's execution to an unauthenticated caller. Mirrors Microsoft.Extensions.AI's own
// ApprovalRequiredAIFunction (DelegatingAIFunction) pattern, applied to "needs a connected account"
// instead of "needs human approval".
public sealed class AuthRequiredAIFunction : DelegatingAIFunction
{
    private readonly Func<CancellationToken, Task<bool>> _isConnectedAsync;
    private readonly string _unauthorizedMessage;

    public AuthRequiredAIFunction(
        AIFunction innerFunction,
        Func<CancellationToken, Task<bool>> isConnectedAsync,
        string unauthorizedMessage)
        : base(innerFunction)
    {
        _isConnectedAsync = isConnectedAsync;
        _unauthorizedMessage = unauthorizedMessage;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var connected = await _isConnectedAsync(cancellationToken);
        return connected
            ? await InnerFunction.InvokeAsync(arguments, cancellationToken)
            : _unauthorizedMessage;
    }
}
