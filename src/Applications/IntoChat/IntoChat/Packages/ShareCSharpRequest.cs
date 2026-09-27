namespace IntoChat.Packages;

// Name defaults to the file id; the package is always owned by the signed-in person.
internal sealed record ShareCSharpRequest(string? Name = null, string? Message = null, IReadOnlyList<DigitalBrain.Apps.PackageAccount>? Accounts = null);
