using DigitalBrain.Apps;
using DigitalBrain.Behavior;

namespace IntoChat.Packages;

internal sealed record InstalledPackageView(AppSnapshot App, BehaviorSnapshot? Behavior);
