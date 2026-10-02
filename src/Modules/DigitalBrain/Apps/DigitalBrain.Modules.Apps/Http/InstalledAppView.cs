using DigitalBrain.Apps;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Apps;

internal sealed record InstalledAppView(AppSnapshot App, IReadOnlyList<CSharpFileSnapshot> Files);
