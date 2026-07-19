using DigitalBrain;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

internal static class QuickstartLiveDriver
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(
            args.Where(argument =>
                    !string.Equals(
                        argument,
                        "--live-driver",
                        StringComparison.Ordinal))
                .ToArray());
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "The DigitalBrain quickstart live driver is disabled outside Development.");

        var ownerValue = builder.Configuration["DigitalBrain:DevTools:Owner"];
        if (string.IsNullOrWhiteSpace(ownerValue))
            throw new InvalidOperationException(
                "Set the explicit digitalbrain-owner Development parameter.");

        builder.AddDigitalBrainClient("brain");
        if (!string.IsNullOrWhiteSpace(
                builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        var app = builder.Build();
        var owner = new BrainOwnerId(ownerValue);

        app.MapHealthChecks("/health");
        app.MapPost(
            "/live/turn",
            async (
                QuickstartLiveTurnRequest request,
                DigitalBrainSessionFactory sessions) =>
            {
                var conversation = new ConversationId(request.Conversation);
                if (!Guid.TryParse(request.TurnId, out var turnValue) ||
                    turnValue == Guid.Empty)
                    return Results.BadRequest("A non-empty turn id is required.");

                await using var session = sessions.Create(owner);
                var turnId = new ConversationTurnId(turnValue);
                var result = await session.Client.Conversations
                    .Balanced(conversation)
                    .SubmitTurnAsync(turnId, request.Text);
                var snapshot = await session.Client.Conversations
                    .Open(conversation)
                    .ReadAsync();
                return Results.Ok(new QuickstartLiveTurnResponse(
                    result.TurnId.ToString(),
                    result.Role.ToString().ToLowerInvariant(),
                    result.Response,
                    result.Revision,
                    snapshot.Turns.Count));
            });
        app.MapGet(
            "/live/conversations/{conversation}",
            async (
                string conversation,
                DigitalBrainSessionFactory sessions) =>
            {
                var conversationId = new ConversationId(conversation);
                await using var session = sessions.Create(owner);
                var snapshot = await session.Client.Conversations
                    .Open(conversationId)
                    .ReadAsync();
                return Results.Ok(new QuickstartLiveSnapshot(
                    snapshot.Revision,
                    snapshot.Turns
                        .Select(turn => new QuickstartLiveTurn(
                            turn.TurnId.ToString(),
                            turn.Role.ToString().ToLowerInvariant(),
                            turn.Text,
                            turn.Response))
                        .ToArray()));
            });

        await app.RunAsync();
    }
}

internal sealed record QuickstartLiveTurnRequest(
    string Conversation,
    string TurnId,
    string Text);

internal sealed record QuickstartLiveTurnResponse(
    string TurnId,
    string Role,
    string Response,
    long Revision,
    int TurnCount);

internal sealed record QuickstartLiveSnapshot(
    long Revision,
    IReadOnlyList<QuickstartLiveTurn> Turns);

internal sealed record QuickstartLiveTurn(
    string TurnId,
    string Role,
    string Text,
    string Response);
