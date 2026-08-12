using System.Globalization;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

internal interface ICellKind
{
    string Name { get; }

    CellState Apply(CellState state, string key);
}

