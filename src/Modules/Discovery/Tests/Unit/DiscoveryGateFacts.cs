using System.Diagnostics;
using DigitalBrain.Apps;
using DigitalBrain.Discovery;
using DigitalBrain.Discovery.Search;
using Xunit;

namespace DigitalBrain.Tests;

// Gate: >= 60 golden prompts vs >= 50 operations (>= 20 distractors); top-5 recall >= 0.9,
// negative precision >= 0.9, p95 < 300 ms, and it still works when embeddings are down.
public sealed class DiscoveryGateFacts
{
    [Fact]
    public async Task KeywordGateMeetsRecallPrecisionAndLatencyWithEmbeddingsDown()
    {
        await RunGateAsync(embed: null, degraded: true);
    }

    [Fact]
    public async Task VectorGateMeetsRecallPrecisionAndLatencyWithEmbeddingsUp()
    {
        await RunGateAsync(embed: HashingEmbed(), degraded: false);
    }

    private static async Task RunGateAsync(Func<string, CancellationToken, ValueTask<float[]?>>? embed, bool degraded)
    {
        var ct = TestContext.Current.CancellationToken;
        var manifests = BuildManifests();
        var index = await CapabilityIndex.BuildAsync([.. manifests.Select(ScopedAppManifest.Global)], embed, ct);

        Assert.True(Targets.Length * 2 + Negatives.Length >= 60, "the golden set must contain at least 60 prompts");
        Assert.True(manifests.Sum(manifest => manifest.Operations.Count) >= 50, "at least 50 operations are required");
        Assert.True(Distractors.Length >= 20, "at least 20 distractor manifests are required");

        var recallHits = 0;
        var latencies = new List<double>();
        foreach (var target in Targets)
        {
            var expected = target.App + "/" + target.Operation;
            foreach (var prompt in new[] { target.Direct, target.Indirect })
            {
                var watch = Stopwatch.StartNew();
                var hits = await index.SearchAsync(prompt, null, 5, degraded, ct);
                latencies.Add(watch.Elapsed.TotalMilliseconds);
                if (hits.Hits.Any(hit => hit.Id == expected))
                {
                    recallHits++;
                }
            }
        }

        var negativeClean = 0;
        foreach (var prompt in Negatives)
        {
            var hits = await index.SearchAsync(prompt, null, 5, degraded, ct);
            if (hits.Hits.Count == 0)
            {
                negativeClean++;
            }
        }

        var positives = Targets.Length * 2;
        var recall = (double)recallHits / positives;
        var precision = (double)negativeClean / Negatives.Length;
        latencies.Sort();
        var p95 = latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];

        Assert.True(recall >= 0.9, $"top-5 recall was {recall:F3}");
        Assert.True(precision >= 0.9, $"negative precision was {precision:F3}");
        Assert.True(p95 < 300, $"p95 latency was {p95:F1} ms");
    }

    private static Func<string, CancellationToken, ValueTask<float[]?>> HashingEmbed()
    {
        var embedder = new HashingCapabilityEmbedder();
        return async (text, ct) => await embedder.EmbedAsync(text, ct);
    }

    private static IReadOnlyList<AppManifest> BuildManifests()
    {
        var manifests = new List<AppManifest>();
        foreach (var target in Targets)
        {
            manifests.Add(new AppManifest
            {
                Id = target.App,
                Version = "1.0.0",
                Publisher = "intochat",
                Kind = AppKind.Declarative,
                Name = target.AppName,
                DescriptionForPeople = target.AppName + " for the workspace.",
                DescriptionForModel = target.AppName + " operations.",
                Operations =
                [
                    new AppOperation
                    {
                        Name = target.Operation,
                        DescriptionForModel = target.Description,
                        ReadOnly = true,
                    },
                ],
            });
        }

        foreach (var distractor in Distractors)
        {
            manifests.Add(new AppManifest
            {
                Id = distractor.App,
                Version = "1.0.0",
                Publisher = "acme",
                Kind = AppKind.Declarative,
                Name = distractor.AppName,
                DescriptionForPeople = distractor.AppName + ".",
                DescriptionForModel = distractor.Description,
                Operations =
                [
                    new AppOperation
                    {
                        Name = distractor.Operation,
                        DescriptionForModel = distractor.Description,
                        ReadOnly = true,
                    },
                ],
            });
        }

        return manifests;
    }

    private static readonly (string App, string AppName, string Operation, string Description, string Direct, string Indirect)[] Targets =
    [
        ("intochat.invoice", "Invoice Desk", "summarize_invoices", "Summarize outstanding invoices into a schedule", "summarize my outstanding invoices", "turn these invoices into a schedule"),
        ("intochat.payroll", "Payroll", "run_payroll", "Run the monthly payroll for staff", "run payroll for this month", "pay the team with payroll"),
        ("intochat.roster", "Roster", "build_roster", "Build the shift roster for the week", "build next week's roster", "who is on the roster tomorrow"),
        ("intochat.ledger", "Ledger", "reconcile_ledger", "Reconcile the general ledger accounts", "reconcile the ledger", "check the ledger balances"),
        ("intochat.refund", "Refunds", "issue_refund", "Issue a refund to a customer", "issue a refund for order 12", "give this customer a refund"),
        ("intochat.expense", "Expenses", "file_expense", "File an expense claim", "file my expense for the trip", "claim this expense"),
        ("intochat.shipment", "Shipments", "track_shipment", "Track a shipment to its destination", "track my shipment", "where is the shipment now"),
        ("intochat.inventory", "Inventory", "count_inventory", "Count the inventory on hand", "count the inventory", "how much inventory is left"),
        ("intochat.reorder", "Reorders", "create_reorder", "Create a reorder for low stock", "create a reorder", "reorder what ran out"),
        ("intochat.supplier", "Suppliers", "rate_supplier", "Rate a supplier on delivery", "rate this supplier", "how is the supplier doing"),
        ("intochat.churn", "Churn", "predict_churn", "Predict churn risk for accounts", "predict churn", "which accounts might churn"),
        ("intochat.ticket", "Tickets", "triage_ticket", "Triage a support ticket", "triage this ticket", "sort out the ticket queue"),
        ("intochat.sprint", "Sprints", "plan_sprint", "Plan the next sprint", "plan the sprint", "what goes in the sprint"),
        ("intochat.roadmap", "Roadmap", "update_roadmap", "Update the product roadmap", "update the roadmap", "add this to the roadmap"),
        ("intochat.candidate", "Candidates", "screen_candidate", "Screen a job candidate", "screen this candidate", "is the candidate a fit"),
        ("intochat.interview", "Interviews", "schedule_interview", "Schedule an interview", "schedule the interview", "book the interview slot"),
        ("intochat.contract", "Contracts", "review_contract", "Review a contract for risks", "review this contract", "any risks in the contract"),
        ("intochat.meeting", "Meetings", "summarize_meeting", "Summarize a meeting into actions", "summarize the meeting", "what came out of the meeting"),
        ("intochat.transcript", "Transcripts", "clean_transcript", "Clean a call transcript", "clean this transcript", "fix the transcript text"),
        ("intochat.newsletter", "Newsletters", "draft_newsletter", "Draft a newsletter", "draft the newsletter", "write the newsletter"),
        ("intochat.campaign", "Campaigns", "launch_campaign", "Launch a marketing campaign", "launch the campaign", "start the campaign"),
        ("intochat.subscriber", "Subscribers", "grow_subscriber", "Grow the subscriber list", "grow our subscriber base", "add a subscriber"),
        ("intochat.budget", "Budgets", "set_budget", "Set a department budget", "set the budget", "what is the budget"),
        ("intochat.variance", "Variance", "explain_variance", "Explain a budget variance", "explain this variance", "why the variance"),
        ("intochat.audit", "Audits", "prepare_audit", "Prepare for an audit", "prepare the audit", "get ready for the audit"),
        ("intochat.compliance", "Compliance", "check_compliance", "Check compliance rules", "check compliance", "are we compliant"),
        ("intochat.renewal", "Renewals", "track_renewal", "Track a contract renewal date", "track the renewal", "when is the renewal"),
        ("intochat.subscription", "Subscriptions", "pause_subscription", "Pause a customer subscription", "pause the subscription", "stop this subscription"),
        ("intochat.chargeback", "Chargebacks", "dispute_chargeback", "Dispute a chargeback case", "dispute this chargeback", "fight the chargeback"),
        ("intochat.dispute", "Disputes", "resolve_dispute", "Resolve a payment dispute", "resolve the dispute", "settle this dispute"),
    ];

    private static readonly (string App, string AppName, string Operation, string Description)[] Distractors =
    [
        ("com.acme.weather", "Weather", "get_forecast", "Get the weather forecast"),
        ("com.acme.recipes", "Recipes", "find_recipe", "Find a cooking recipe"),
        ("com.acme.music", "Music", "tune_guitar", "Tune a guitar to pitch"),
        ("com.acme.travel", "Travel", "book_hotel", "Book a hotel room"),
        ("com.acme.fitness", "Fitness", "track_stamina", "Track stamina during workouts"),
        ("com.acme.garden", "Garden", "water_plant", "Water the garden plants"),
        ("com.acme.puzzles", "Puzzles", "solve_crossword", "Solve a crossword clue"),
        ("com.acme.laundry", "Laundry", "sort_laundry", "Sort the laundry by colour"),
        ("com.acme.grooming", "Grooming", "book_haircut", "Book a haircut appointment"),
        ("com.acme.cycling", "Cycling", "repair_bicycle", "Repair a bicycle tyre"),
        ("com.acme.art", "Art", "paint_painting", "Paint a landscape painting"),
        ("com.acme.crafts", "Crafts", "knit_knitting", "Knit a woollen scarf"),
        ("com.acme.astronomy", "Astronomy", "aim_telescope", "Aim the telescope at stars"),
        ("com.acme.fish", "Fish", "clean_aquarium", "Clean the aquarium tank"),
        ("com.acme.sewing", "Sewing", "sew_sewing", "Sew a button"),
        ("com.acme.pets", "Pets", "walk_dog", "Walk the dog"),
        ("com.acme.baking", "Baking", "bake_bread", "Bake sourdough bread"),
        ("com.acme.coffee", "Coffee", "brew_coffee", "Brew an espresso"),
        ("com.acme.yoga", "Yoga", "hold_pose", "Hold a yoga pose"),
        ("com.acme.photo", "Photos", "crop_photo", "Crop a photograph"),
        ("com.acme.board", "Boards", "play_chess", "Play a game of chess"),
        ("com.acme.woodwork", "Woodwork", "sand_wood", "Sand a wooden plank"),
        ("com.acme.fishing", "Fishing", "cast_line", "Cast a fishing line"),
        ("com.acme.pottery", "Pottery", "throw_clay", "Throw a clay pot"),
        ("com.acme.radio", "Radio", "tune_radio", "Tune a radio station"),
    ];

    private static readonly string[] Negatives =
    [
        "teleport me to the moon",
        "sing with a narwhal",
        "pour some lava",
        "fold an origami crane",
        "tame a dragon",
        "spread marmalade",
        "ride a unicorn",
        "map the nebula",
        "whittle a harpsichord",
        "study a quasar",
        "yodel from the cliff",
        "carve a gargoyle",
        "observe a bobcat",
        "squeeze an accordion",
        "fly a zeppelin",
    ];
}
