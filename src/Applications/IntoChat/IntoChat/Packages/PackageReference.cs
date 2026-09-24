namespace IntoChat.Packages;

// Revision null means the package's latest: its published revision for installs, its head for contributions.
internal sealed record PackageReference(string Owner, string Name, string? Revision = null);
