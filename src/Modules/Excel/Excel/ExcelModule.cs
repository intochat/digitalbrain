using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Excel;

public sealed class ExcelModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(ExcelModule));

    public void Configure(ISiloBuilder silo) { }
}