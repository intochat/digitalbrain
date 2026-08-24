using DigitalBrain.Client;
using DigitalBrain.SmartPrompt;

namespace DigitalBrain.Kernel;

internal static class BehaviorHttpMaps
{
    public static IEndpointRouteBuilder MapBehaviors(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(HttpSurfacePaths.BehaviorsPath,
            static async Task<IResult> (IDigitalBrain brain) =>
            {
                var summaries = new List<BehaviorSummary>(BehaviorExamples.All.Count);
                foreach (var example in BehaviorExamples.All)
                {
                    var state = await brain.GetEntity<IBehaviorDefinition>(example.Name).Read();
                    summaries.Add(Summary(example.Name, example.Title, example.Source, state));
                }
                return Results.Ok(summaries);
            });

        endpoints.MapGet(HttpSurfacePaths.BehaviorStepsPath,
            static (IBehaviorCompiler compiler) => Results.Ok(compiler.Suggestions));

        endpoints.MapPost(HttpSurfacePaths.BehaviorGeneratePath,
            static async Task<IResult> (BehaviorGenerateRequest request, IBehaviorFeatureGenerator generator,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Request))
                {
                    return Results.BadRequest(new { error = "request must not be blank" });
                }
                return Results.Ok(await generator.Generate(request.Request, cancellationToken));
            });

        endpoints.MapGet(HttpSurfacePaths.BehaviorPath,
            static async Task<IResult> (string name, IDigitalBrain brain) =>
            {
                if (!ValidName(name))
                {
                    return Results.BadRequest();
                }
                var state = await brain.GetEntity<IBehaviorDefinition>(name).Read();
                if (state is null)
                {
                    return Results.NotFound();
                }
                var example = BehaviorExamples.Find(name);
                return Results.Ok(Summary(name, example?.Title ?? state.Compilation.Plan?.Feature ?? name, state.Source, state));
            });

        endpoints.MapPut(HttpSurfacePaths.BehaviorPath,
            static async Task<IResult> (string name, BehaviorSaveRequest request, IDigitalBrain brain) =>
            {
                if (!ValidName(name) || string.IsNullOrWhiteSpace(request.Source))
                {
                    return Results.BadRequest();
                }
                var compilation = await brain.GetEntity<IBehaviorDefinition>(name).Save(request.Source);
                return Results.Ok(compilation);
            });

        endpoints.MapPost(HttpSurfacePaths.BehaviorTestPath,
            static async Task<IResult> (string name, IDigitalBrain brain) =>
                !ValidName(name) ? Results.BadRequest() : Results.Ok(await brain.GetEntity<IBehaviorDefinition>(name).Test()));

        endpoints.MapPost(HttpSurfacePaths.BehaviorActivatePath,
            static async Task<IResult> (string name, IDigitalBrain brain) =>
            {
                if (!ValidName(name))
                {
                    return Results.BadRequest();
                }
                await brain.GetEntity<IBehaviorDefinition>(name).Activate();
                return Results.Ok(new { active = true });
            });

        endpoints.MapPost(HttpSurfacePaths.BehaviorDisablePath,
            static async Task<IResult> (string name, IDigitalBrain brain) =>
            {
                if (!ValidName(name))
                {
                    return Results.BadRequest();
                }
                await brain.GetEntity<IBehaviorDefinition>(name).Disable();
                return Results.Ok(new { active = false });
            });

        endpoints.MapPost(HttpSurfacePaths.BehaviorFakePath,
            static async Task<IResult> (string name, IDigitalBrain brain, IGrainFactory grains) =>
            {
                if (!ValidName(name))
                {
                    return Results.BadRequest();
                }
                var example = BehaviorExamples.Find(name);
                if (example is null)
                {
                    return Results.NotFound();
                }
                var definition = brain.GetEntity<IBehaviorDefinition>(name);
                var state = await definition.Read();
                if (state is null)
                {
                    await definition.Save(example.Source);
                    await definition.Test();
                    await definition.Activate();
                }
                else if (!state.Active)
                {
                    await definition.Activate();
                }
                var behaviorEvent = FakeBehaviorEvents.Create(name);
                await grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared).Publish(behaviorEvent);
                return Results.Ok(new BehaviorFakeResponse(behaviorEvent.EventId, FakeBehaviorEvents.Describe(behaviorEvent)));
            });

        return endpoints;
    }

    private static BehaviorSummary Summary(string name, string title, string source, BehaviorDefinitionState? state)
        => new(name, title, source, state?.Active ?? false, state?.LastTest,
            state?.Compilation.Diagnostics ?? []);

    private static bool ValidName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Length <= 80
           && name.All(static c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c == '-');
}

internal sealed record BehaviorSaveRequest(string Source);
internal sealed record BehaviorGenerateRequest(string Request);
internal sealed record BehaviorFakeResponse(string EventId, string Description);
