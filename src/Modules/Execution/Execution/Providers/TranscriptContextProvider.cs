using DigitalBrain.Chat;

namespace DigitalBrain.Execution;

public sealed class TranscriptContextProvider(IGrainFactory grains) : IExecutionContextProvider
{
    private const int LastTurnCount = 8;

    public async Task ContributeAsync(ExecutionSeedBuilder seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        if (seed.Workload is not ChatTurnWorkload chatTurn)
        {
            return;
        }

        seed.PromptBlocks.Add($"Current user turn: {chatTurn.UserText}");

        try
        {
            var chat = grains.GetGrain<IChat>(chatTurn.ChatId.ToGrainId());
            var turns = await chat.ReadTurns()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (turns.Count == 0)
            {
                return;
            }

            var start = Math.Max(0, turns.Count - LastTurnCount);
            var lines = new List<string>(turns.Count - start);
            for (var i = start; i < turns.Count; i++)
            {
                lines.Add($"- {turns[i].Status}: {turns[i].Text}");
            }

            seed.PromptBlocks.Add("Recent chat turns:\n" + string.Join('\n', lines));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chat neuron may be absent when UI module is not loaded.
        }
    }
}