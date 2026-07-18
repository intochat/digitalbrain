using System.Text.Json;
using DigitalBrain.FeatureBuilder;
if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("FeatureBuilder requires one JSON request path.");
    return 2;
}
try
{
    await using var stream = File.OpenRead(Path.GetFullPath(args[0]));
    var command = await JsonSerializer.DeserializeAsync<FeatureBuildCommand>(stream)
        ?? throw new InvalidDataException("The build request is empty.");
    var files = command.Files.Select(file => new FeatureSourceFile(file.Path, Convert.FromBase64String(file.ContentBase64))).ToArray();
    var snapshot = new FeatureSourceSnapshot(command.ImplementationProjectPath, command.ScenarioProjectPath, files);
    var request = new FeatureBuildRequest(snapshot, command.OfflineFeedDirectory, command.OutputDirectory, command.Deadline);
    var verification = await new FeatureBuildPipeline().VerifyAsync(request);
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(verification));
    return 0;
}
catch (Exception exception) when (exception is FeatureBuildException or ArgumentException or InvalidDataException or JsonException or FormatException or IOException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}
internal sealed record FeatureBuildCommand(
    string ImplementationProjectPath,
    string ScenarioProjectPath,
    IReadOnlyList<FeatureBuildCommandFile> Files,
    string OfflineFeedDirectory,
    string OutputDirectory,
    DateTimeOffset Deadline);
internal sealed record FeatureBuildCommandFile(string Path, string ContentBase64);
