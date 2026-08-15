namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.enable-library-install")]
public sealed record EnableLibraryInstall(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string InstallId,
    // Principal-local config (e.g. numbers) so two installs differ.
    [property: Id(2)] string? ConfigJson = null,
    [property: Id(3)] ActorContext? Actor = null) : RequestSynapse<LibraryInstallEnabled>;

