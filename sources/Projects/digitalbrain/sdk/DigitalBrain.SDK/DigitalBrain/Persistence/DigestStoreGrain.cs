using System.Text.Json;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Google;
using Microsoft.EntityFrameworkCore;

namespace DigitalBrain.SDK.DigitalBrain.Persistence;

[GrainType("DigitalBrain.Digest.DigestStore")]
public sealed class DigestStoreGrain(
    IDbContextFactory<SynapseDbContext> dbContextFactory,
    ILogger<DigestStoreGrain> logger)
    : Grain, ICallNeuronTarget
{
    public async Task<string> AskAsync(string prompt)
    {
        if (prompt == "get-gmail-digest")
        {
            logger.LogInformation("DigestStore: Querying persisted Gmail digest synapses from SynapseDbContext...");

            List<SynapseEntity> entities;
            try
            {
                using var context = await dbContextFactory.CreateDbContextAsync();
                await context.Database.EnsureCreatedAsync();
                
                // Get all stored GmailSendersReady synapses
                var rawEntities = await context.Synapses
                    .Where(s => s.ReceiverNeuronType == "GmailDigestNeuron" || s.ReceiverNeuronType == "GmailDigestNeuronType" || s.ReceiverNeuronType == "google/gmail-digest")
                    .ToListAsync();

                entities = rawEntities
                    .OrderByDescending(s => s.Timestamp)
                    .Take(5)
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DigestStore: Exception occurred while querying SynapseDbContext.");
                throw;
            }

            var allSenders = new List<GmailSender>();
            foreach (var entity in entities)
            {
                if (string.IsNullOrEmpty(entity.PayloadJson)) continue;
                try
                {
                    var ready = JsonSerializer.Deserialize<GmailSendersReady>(entity.PayloadJson);
                    if (ready?.Senders != null)
                    {
                        allSenders.AddRange(ready.Senders);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "DigestStore: Failed to deserialize GmailSendersReady payload for SynapseId {SynapseId}", entity.SynapseId);
                }
            }

            // Deduplicate senders by email address and sort by ReceivedUtc descending
            var uniqueSenders = allSenders
                .GroupBy(s => s.EmailAddress)
                .Select(g => g.OrderByDescending(s => s.ReceivedUtc).First())
                .OrderByDescending(s => s.ReceivedUtc)
                .Take(5)
                .ToList();

            logger.LogInformation("DigestStore: Found {Count} unique senders in persisted synapses.", uniqueSenders.Count);

            // Construct premium Generative UI payload with a custom HSL dark mode, glassmorphism card!
            var cardSource = @"
import digitalbrain;

widget root = Panel(
  padding: 20.0,
  child: VStack(
    gap: 16.0,
    cross: ""stretch"",
    children: [
      HStack(
        between: true,
        children: [
          VStack(
            cross: ""start"",
            gap: 2.0,
            children: [
              Text(text: ""Gmail Senders Digest"", variant: ""title""),
              Text(text: ""Directly synchronized from secure SQLite layer"", variant: ""dim""),
            ],
          ),
          Badge(text: data.countLabel, tone: ""teal""),
        ],
      ),
      Divider(),
      ...for s in data.senders:
        Panel(
          padding: 12.0,
          child: HStack(
            between: true,
            children: [
              VStack(
                cross: ""start"",
                gap: 4.0,
                children: [
                  HStack(
                    gap: 8.0,
                    children: [
                      GlowIcon(icon: ""mail"", tone: ""teal"", size: 14.0),
                      Text(text: s.name, variant: ""body""),
                    ],
                  ),
                  Text(text: s.email, variant: ""mono""),
                  Text(text: s.subject, variant: ""dim""),
                ],
              ),
              Tag(text: s.timeLabel, tone: ""rose""),
            ],
          ),
        ),
    ],
  ),
);
";

            var senderPayload = uniqueSenders.Select(s => new
            {
                name = string.IsNullOrEmpty(s.Name) ? "Unknown" : s.Name,
                email = s.EmailAddress,
                subject = string.IsNullOrEmpty(s.Subject) ? "(No Subject)" : s.Subject,
                timeLabel = s.ReceivedUtc.ToString("h:mm tt")
            }).ToList();

            var countLabel = uniqueSenders.Count == 1 ? "1 active sender" : $"{uniqueSenders.Count} active senders";

            var responseJson = JsonSerializer.Serialize(new
            {
                countLabel = countLabel,
                senders = senderPayload,
                source = cardSource
            });

            return responseJson;
        }

        return "";
    }
}
