namespace DigitalBrain.Kernel;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

public sealed class AuthRequiredAIFunction : DelegatingAIFunction
{
    private readonly Func<CancellationToken, Task<bool>> _isConnectedAsync;
    private readonly string _unauthorizedMessage;
    private readonly Func<CancellationToken, Task>? _onAuthRequired;

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

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
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

        return _unauthorizedMessage;
    }
}
