using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class AuthRequiredAIFunctionTests
{
    [Fact]
    public async Task Invokes_inner_function_when_connected()
    {
        var callCount = 0;
        var inner = AIFunctionFactory.Create(
            () =>
            {
                callCount++;
                return "real result";
            },
            name: "inner_tool");

        var gated = new AuthRequiredAIFunction(inner, _ => Task.FromResult(true), "unauthorized");

        var result = await gated.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        Assert.Equal("real result", result?.ToString());
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Returns_unauthorized_message_and_never_calls_inner_function_when_not_connected()
    {
        var callCount = 0;
        var inner = AIFunctionFactory.Create(
            () =>
            {
                callCount++;
                return "real result";
            },
            name: "inner_tool");

        var gated = new AuthRequiredAIFunction(inner, _ => Task.FromResult(false), "please connect your account first");

        var result = await gated.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        Assert.Equal("please connect your account first", result);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task Exposes_inner_function_name_and_description_unchanged()
    {
        var inner = AIFunctionFactory.Create(
            () => "result",
            name: "inner_tool",
            description: "does a thing");

        var gated = new AuthRequiredAIFunction(inner, _ => Task.FromResult(true), "unauthorized");

        Assert.Equal("inner_tool", gated.Name);
        Assert.Equal("does a thing", gated.Description);
    }
}
