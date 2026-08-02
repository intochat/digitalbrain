using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace DigitalBrain.Mcp.Aspire.Hosting;

internal sealed class ConstantParameterDefault(string value) : ParameterDefault
{
    public override string GetDefaultValue() => value;

    public override void WriteToManifest(ManifestPublishingContext context)
    {
    }
}

internal sealed class OperatorSuppliedParameterDefault(string parameterName) : ParameterDefault
{
    public override string GetDefaultValue()
        => throw new MissingParameterValueException(
            $"Parameter '{parameterName}' has no value. Set it in the Aspire dashboard (Save to user secrets) "
            + $"or: dotnet user-secrets set \"Parameters:{parameterName}\" \"<value>\" --project os/DigitalBrain.OS.AppHost");

    public override void WriteToManifest(ManifestPublishingContext context)
        => throw new InvalidOperationException(
            $"Parameter '{parameterName}' is operator-supplied and cannot be published as a default.");
}
