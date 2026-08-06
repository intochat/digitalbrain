namespace DigitalBrain.Testing;

internal sealed record TestComposition(
    IReadOnlyList<Action<DigitalBrainComposition>> Registrations,
    IReadOnlyList<string> RegistrationRows,
    IReadOnlyList<(Type Contract, object Instance)> Services)
{
    internal void Configure(DigitalBrainComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        foreach (var registration in Registrations)
        {
            registration(composition);
        }
    }

    internal string Fingerprint()
    {
        var registrations = RegistrationRows.Order(StringComparer.Ordinal);
        var serviceRows = Services
            .Select(service => $"{service.Contract.FullName}={service.Instance.GetType().FullName}")
            .Order(StringComparer.Ordinal);
        return string.Join('|', [.. registrations, .. serviceRows]);
    }
}
