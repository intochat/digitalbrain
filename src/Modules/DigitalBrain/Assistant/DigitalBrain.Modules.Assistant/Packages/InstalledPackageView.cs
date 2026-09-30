using DigitalBrain.Apps;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Assistant;

internal sealed record InstalledPackageView(AppSnapshot App, IReadOnlyList<CSharpFileSnapshot> Files);
