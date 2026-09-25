using System.Text.Json;

namespace DigitalBrain.CSharpExpert;

internal static class CodingPlanReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CodingPlan Read(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new FormatException("The planner returned an empty reply.");
        }

        PlanPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PlanPayload>(reply, Options);
        }
        catch (JsonException error)
        {
            throw new FormatException($"The planner reply is not valid JSON: {error.Message}", error);
        }

        if (payload is null)
        {
            throw new FormatException("The planner reply is not a JSON object.");
        }

        if (string.IsNullOrWhiteSpace(payload.Summary))
        {
            throw new FormatException("The planner reply has no summary.");
        }

        if (payload.Steps is not { Count: > 0 })
        {
            throw new FormatException("The planner reply has no steps.");
        }

        var steps = payload.Steps
            .Select((step, index) => new PlanStep(
                step.Number > 0 ? step.Number : index + 1,
                step.Title ?? string.Empty,
                step.Files ?? [],
                step.Detail ?? string.Empty))
            .ToArray();
        return new CodingPlan(payload.Summary, steps, payload.OpenQuestions ?? []);
    }

    private sealed record PlanPayload(string? Summary, IReadOnlyList<StepPayload>? Steps, IReadOnlyList<string>? OpenQuestions);

    private sealed record StepPayload(int Number, string? Title, IReadOnlyList<string>? Files, string? Detail);
}
