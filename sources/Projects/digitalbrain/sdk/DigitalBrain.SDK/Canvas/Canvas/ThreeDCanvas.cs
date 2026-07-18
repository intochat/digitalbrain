using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using Orleans.Journaling;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Canvas.Canvas;

[GrainType(NeuronTargetFqn)]
internal sealed class Canvas3D : Neuron, ICallNeuronTarget
{
    public const string NeuronTargetFqn = "DigitalBrain.Canvas3D";

    private readonly IDurableList<Atom3D> _atoms;
    private readonly IDurableList<Bond3D> _bonds;
    private double _spinSpeed = 1.0;

    public Canvas3D(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger<Canvas3D> logger,
        [FromKeyedServices("canvas-3d-atoms")] IDurableList<Atom3D> atoms,
        [FromKeyedServices("canvas-3d-bonds")] IDurableList<Bond3D> bonds)
        : base(incoming, outgoing, grains, logger)
    {
        _atoms = atoms;
        _bonds = bonds;
    }

    public async Task<string> AskAsync(string prompt)
    {
        var cleaned = prompt.Trim().ToLowerInvariant();

        if (cleaned == "clear")
        {
            _atoms.Clear();
            _bonds.Clear();
            _spinSpeed = 1.0;
            await WriteStateAsync();
            await PushRfwUiAsync();
            return "ok";
        }

        if (cleaned.StartsWith("add atom:", StringComparison.Ordinal))
        {
            // Format: "add atom: O at x, y, z"
            var parts = prompt["add atom:".Length..].Split("at", StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var symbol = parts[0];
                var coords = parts[1].Split(',', StringSplitOptions.TrimEntries);
                if (coords.Length == 3 &&
                    double.TryParse(coords[0], out var x) &&
                    double.TryParse(coords[1], out var y) &&
                    double.TryParse(coords[2], out var z))
                {
                    var color = symbol.ToUpperInvariant() switch
                    {
                        "O" => "red",
                        "H" => "white",
                        "C" => "grey",
                        "N" => "blue",
                        _ => "indigo"
                    };
                    var radius = symbol.ToUpperInvariant() switch
                    {
                        "O" => 24.0,
                        "H" => 16.0,
                        "C" => 28.0,
                        _ => 20.0
                    };

                    _atoms.Add(new Atom3D(symbol, x, y, z, color, radius));
                    await WriteStateAsync();
                    await PushRfwUiAsync();
                    return "ok";
                }
            }
            return "error:invalid atom format. Expected 'add atom: O at 0, 0, 0'";
        }

        if (cleaned.StartsWith("add bond between", StringComparison.Ordinal))
        {
            // Format: "add bond between 0 and 1"
            var parts = cleaned["add bond between".Length..].Split("and", StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var from) &&
                int.TryParse(parts[1], out var to))
            {
                _bonds.Add(new Bond3D(from, to));
                await WriteStateAsync();
                await PushRfwUiAsync();
                return "ok";
            }
            return "error:invalid bond format. Expected 'add bond between 0 and 1'";
        }

        if (cleaned.StartsWith("spin speed:", StringComparison.Ordinal))
        {
            // Format: "spin speed: 1.5"
            var valStr = cleaned["spin speed:".Length..].Trim();
            if (double.TryParse(valStr, out var speed))
            {
                _spinSpeed = speed;
                await PushRfwUiAsync();
                return "ok";
            }
            return "error:invalid spin speed. Expected 'spin speed: 1.5'";
        }

        return "error:unknown command";
    }

    private async Task PushRfwUiAsync()
    {
        var atomList = new List<object>();
        foreach (var a in _atoms)
        {
            atomList.Add(new
            {
                symbol = a.Symbol,
                x = a.X,
                y = a.Y,
                z = a.Z,
                color = a.Color,
                radius = a.Radius
            });
        }

        var bondList = new List<object>();
        foreach (var b in _bonds)
        {
            bondList.Add(new
            {
                from = b.From,
                to = b.To
            });
        }

        var payload = new
        {
            sceneName = this.GetPrimaryKeyString(),
            spinSpeed = _spinSpeed,
            atoms = atomList,
            bonds = bondList
        };

        var dataJson = JsonSerializer.Serialize(payload);

        var card = new RfwCard(
            LibraryName: "digitalbrain",
            RootWidget: "Canvas3D",
            DataJson: dataJson
        )
        {
            Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: Guid.NewGuid(),
                causationId: null,
                callerNeuronId: InstanceId,
                callerNeuronType: NeuronType,
                receiverNeuronId: Guid.Empty,
                receiverNeuronType: "HomeFeed",
                timestamp: DateTimeOffset.UtcNow
            )
        };

        await FireSynapseAsync(card);
    }
}
