namespace IntoChat.Packages;

// Name defaults to the automation id; the package is always owned by the signed-in person.
internal sealed record ShareSynapseRequest(string? Name = null, string? Message = null, IReadOnlyList<DigitalBrain.Apps.PackageAccount>? Accounts = null);
