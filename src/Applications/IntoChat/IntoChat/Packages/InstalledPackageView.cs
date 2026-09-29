using DigitalBrain.Apps;
using DigitalBrain.Microsoft.CSharp;

namespace IntoChat.Packages;

internal sealed record InstalledPackageView(AppSnapshot App, IReadOnlyList<CSharpFileSnapshot> Files);
