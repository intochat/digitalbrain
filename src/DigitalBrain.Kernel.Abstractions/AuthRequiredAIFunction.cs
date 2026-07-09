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
    private readonly Func<CancellationToken, Task>? _onAuthRequired;

    // The wrapper used by Ino reads this immediately after an invocation. A
    // tool instance is created per request, so this does not leak state between
    // requests and lets auth failures be distinguished from successful results.
    public bool LastInvocationRequiredAuthentication { get; private set; }

    public AuthRequiredAIFunction(
        AIFunction innerFunction,
        Func<CancellationToken, Task<bool>> isConnectedAsync,
        string unauthorizedMessage,
        Func<CancellationToken, Task>? onAuthRequired = null)
        : base(innerFunction)
    {
        _isConnectedAsync = isConnectedAsync;
        _unauthorizedMessage = unauthorizedMessage;
        _onAuthRequired = onAuthRequired;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        LastInvocationRequiredAuthentication = false;
        var connected = await _isConnectedAsync(cancellationToken);
        if (connected)
        {
            return await InnerFunction.InvokeAsync(arguments, cancellationToken);
        }

        LastInvocationRequiredAuthentication = true;

        if (_onAuthRequired != null)
        {
            await _onAuthRequired(cancellationToken);
        }

        // Phase 1: typed path (Success/NeedsAuth etc) - see ToolResult in Core. Current returns string for LLM compat; full switch in composer.
        return _unauthorizedMessage;
    }
}
