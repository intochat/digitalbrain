using DigitalBrain.Client;
using DigitalBrain.SmartPrompt;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class BehaviorHttpMaps
{
    public static IEndpointRouteBuilder MapBehaviors(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(HttpSurfacePaths.BehaviorsPath,
            static async Task<IResult> (IDigitalBrain brain) =>
            {
                var catalog = await brain.GetEntity<IBehaviorCatalog>("catalog").Read();
                var names = BehaviorExamples.All.Select(static example => example.Name)
                    .Concat(catalog?.Names ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal);
                var summaries = new List<BehaviorSummary>();
                foreach (var name in names)
                {
                    var example = BehaviorExamples.Find(name);
                    var state = await brain.GetEntity<IBehaviorDefinition>(name).Read();
                    if (state is not null || example is not null)
                    {
                        summaries.Add(Summary(name, example?.Title ?? state!.Compilation.Plan?.Feature ?? name,
                            state?.Source ?? example!.Source, state));
                    }
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
                await brain.GetEntity<IBehaviorCatalog>("catalog").Add(name);
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

        endpoints.MapGet(HttpSurfacePaths.BehaviorChartPath,
            static async Task<IResult> (string chartName, IDigitalBrain brain) =>
            {
                if (!ValidName(chartName.Replace('_', '-')))
                {
                    return Results.BadRequest();
                }
                var chart = await brain.GetEntity<IChart>(chartName).Read();
                return chart is null ? Results.NotFound() : Results.Ok(chart);
            });

        endpoints.MapPost(HttpSurfacePaths.XPostIngressPath,
            static async Task<IResult> (
                XPostIngressRequest post,
                HttpRequest request,
                IConfiguration configuration,
                IGrainFactory grains) =>
            {
                var expectedKey = configuration["DigitalBrain:Behaviors:IngressKey"];
                if (!string.IsNullOrEmpty(expectedKey)
                    && !string.Equals(request.Headers["X-DigitalBrain-Ingress-Key"], expectedKey, StringComparison.Ordinal))
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(post.Id)
                    || string.IsNullOrWhiteSpace(post.Account)
                    || string.IsNullOrWhiteSpace(post.Text)
                    || !Uri.TryCreate(post.SourceUri, UriKind.Absolute, out _))
                {
                    return Results.BadRequest(new { error = "id, account, text, and an absolute sourceUri are required" });
                }
                var behaviorEvent = new BehaviorEvent(
                    post.Id.Trim(),
                    "x.post",
                    post.Account.Trim().TrimStart('@'),
                    post.Text.Trim(),
                    post.Value,
                    post.SourceUri,
                    post.OccurredAt ?? DateTimeOffset.UtcNow);
                await grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared).Publish(behaviorEvent);
                return Results.Accepted(value: new BehaviorFakeResponse(
                    behaviorEvent.EventId,
                    FakeBehaviorEvents.Describe(behaviorEvent)));
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
internal sealed record XPostIngressRequest(
    string Id,
    string Account,
    string Text,
    double Value,
    string SourceUri,
    DateTimeOffset? OccurredAt);
