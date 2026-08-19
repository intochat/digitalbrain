using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Pins the per-capability default-instance mechanism SystemTools.ResolveTarget relies on: one
// neuron contract (IUIRenderer) serves several capabilities that do not all want the same
// default instance -- an untargeted fire("ui.open-surface") must still reach the "desk" surface
// SurfaceBoot opens and the shell watches, even though the renderer's own default is "default".
public sealed class CapabilityDefaultInstanceTests
{
    [Fact]
    public void OpenSurfaceDeclaresItsOwnDefaultInstanceOverridingTheRenderersDefault()
    {
        var manifest = ModuleReflection.ManifestOf(typeof(IUIRenderer).Assembly);
        var renderer = manifest.Neurons.Single(neuron => neuron.ContractId == "ui.renderer");
        var openSurface = renderer.Accepted.Single(accepted => accepted.ContractId == "ui.open-surface");

        Assert.Equal(ISurface.DefaultInstanceName, openSurface.DefaultInstanceName);
        Assert.NotEqual(renderer.DefaultInstanceName, openSurface.DefaultInstanceName);
    }

    [Fact]
    public void ChartPointKeepsTheRenderersOwnDefaultInstance()
    {
        var manifest = ModuleReflection.ManifestOf(typeof(IUIRenderer).Assembly);
        var renderer = manifest.Neurons.Single(neuron => neuron.ContractId == "ui.renderer");
        var chartPoint = renderer.Accepted.Single(accepted => accepted.ContractId == "ui.chart-point");

        Assert.Null(chartPoint.DefaultInstanceName);
    }
}
