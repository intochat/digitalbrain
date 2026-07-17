# Build your first module

Extend the current universal-grain model: a typed `INeuronContract` for clients, an `INeuronKind` for behavior, explicit host composition, and conformance tests.

## 1. Define the typed façade

Place the interface with the module:

```csharp
public sealed record EchoRequest(string Text);
public sealed record EchoReceipt(string Text);

public interface IEcho : INeuronContract
{
    [NeuronContract("echo.repeat.v1")]
    Task<EchoReceipt> RepeatAsync(EchoRequest request);
}
```

`NeuronProxy` currently supports one request argument and `Task<TResult>`.

## 2. Implement the kind

```csharp
public sealed class EchoKind : INeuronKind
{
    public string Kind => "echo";
    public string[] Contracts => ["echo.repeat.v1"];

    public ValueTask<KindResult> InvokeAsync(
        NeuronContext context,
        NeuronInvocation invocation)
    {
        var request = JsonSerializer.Deserialize<EchoRequest>(invocation.InputJson)!;
        var output = JsonSerializer.Serialize(new EchoReceipt(request.Text));
        return ValueTask.FromResult(
            new KindResult(output, [("echo.repeated", invocation.InputJson)]));
    }

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new { revision = context.Revision });
}
```

Validate malformed and oversized input with the shared `BrainException` error vocabulary. Keep provider clients and nondeterministic work outside the kind.

## 3. Compose it in the host

Add the project reference, then register the kind or a module hosting extension in `hosts/Brain.Kernel.Host/Program.cs`.

```csharp
silo.AddBrainKernel(
    new ChatKind(),
    new WindowKind(),
    new FeedKind(),
    new EchoKind());
```

This explicit host composition is the implemented module-loading model.

## 4. Test the shared invariants

Add module coverage under `tests/Brain.ConformanceTests`. At minimum, verify:

- The advertised contract invokes successfully.
- An unknown contract returns the shared error.
- A repeated command identifier replays the same receipt.
- Invalid input is rejected without a journal event.
- The projection reconstructs from journaled events.

Run:

```powershell
dotnet test tests/Brain.ConformanceTests/Brain.ConformanceTests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

## 5. Update the evidence ledger

Document the kind under [Implementation status](/reference/status). Do not claim manifests, dynamic loading, sandboxing, authenticated callers, or production durability unless the change implements and tests them.
