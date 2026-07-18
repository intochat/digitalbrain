using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed record InstalledState(IReadOnlyList<DomainId> Installed);
