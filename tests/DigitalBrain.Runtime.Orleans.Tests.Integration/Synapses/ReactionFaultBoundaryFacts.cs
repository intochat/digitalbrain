using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class ReactionFaultBoundaryFacts
{
    private sealed record TestState : NeuronState
    {
        public int ProcessedCount { get; init; }
        public List<string> ProcessedNotes { get; init; } = [];
    }

    [Fact]
    public async Task Transient_error_retries_3_times_with_backoff_and_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        TestState? committed = null;

        var initial = new TestState { SignalCursor = 0, ProcessedCount = 0 };
        var signal = new Note("retry-test");
        const long position = 42;

        var (finalState, succeeded) = await ReactionFaultBoundary.ExecuteReactionAsync(
            initial,
            position,
            "b1/chat/p",
            signal,
            (state, sig, token) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException($"Simulated transient error attempt {attempts}");
                }

                return Task.FromResult(state with { ProcessedCount = state.ProcessedCount + 1 });
            },
            (state, token) =>
            {
                committed = state;
                return Task.CompletedTask;
            },
            time: TimeProvider.System,
            maxRetries: 3,
            backoff: attempt => TimeSpan.Zero,
            ct: ct);

        Assert.True(succeeded);
        Assert.Equal(3, attempts);
        Assert.Equal(position, finalState.SignalCursor);
        Assert.Equal(1, finalState.ProcessedCount);
        Assert.Empty(finalState.DeadLetters);
        Assert.NotNull(committed);
        Assert.Equal(position, committed.SignalCursor);
        Assert.Equal(1, committed.ProcessedCount);
    }

    [Fact]
    public async Task Poison_pill_records_dead_letter_and_advances_cursor()
    {
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        TestState? committed = null;

        var initial = new TestState { SignalCursor = 10 };
        var signal = new Note("poison-content");
        const long position = 11;
        const string publisher = "b1/chat/poisoner";

        var (finalState, succeeded) = await ReactionFaultBoundary.ExecuteReactionAsync(
            initial,
            position,
            publisher,
            signal,
            (state, sig, token) =>
            {
                attempts++;
                throw new InvalidOperationException("Fatal deserialization or semantic poison error");
            },
            (state, token) =>
            {
                committed = state;
                return Task.CompletedTask;
            },
            time: TimeProvider.System,
            maxRetries: 3,
            backoff: attempt => TimeSpan.Zero,
            ct: ct);

        Assert.False(succeeded);
        Assert.Equal(3, attempts);
        Assert.Equal(position, finalState.SignalCursor);
        var deadLetter = Assert.Single(finalState.DeadLetters);
        Assert.Equal(position, deadLetter.Position);
        Assert.Equal(publisher, deadLetter.Publisher);
        Assert.Equal(typeof(Note).FullName, deadLetter.SignalType);
        Assert.Contains("Fatal deserialization or semantic poison error", deadLetter.ErrorMessage);
        Assert.NotNull(committed);
        Assert.Equal(position, committed.SignalCursor);
        Assert.Single(committed.DeadLetters);
    }

    [Fact]
    public async Task Poison_pill_does_not_halt_subsequent_signals()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new TestState { SignalCursor = 100 };
        var signals = new (long Pos, Signal Sig)[]
        {
            (101, new Note("good-1")),
            (102, new Note("poison-pill")),
            (103, new Note("good-2"))
        };

        foreach (var (pos, sig) in signals)
        {
            var note = (Note)sig;
            var (nextState, _) = await ReactionFaultBoundary.ExecuteReactionAsync(
                state,
                pos,
                "b1/chat/sender",
                sig,
                (s, currentSig, token) =>
                {
                    if (note.Text == "poison-pill")
                    {
                        throw new InvalidOperationException("Poison pill detected");
                    }

                    var updatedNotes = new List<string>(s.ProcessedNotes) { note.Text };
                    return Task.FromResult(s with
                    {
                        ProcessedCount = s.ProcessedCount + 1,
                        ProcessedNotes = updatedNotes
                    });
                },
                (committedState, token) => Task.CompletedTask,
                time: TimeProvider.System,
                maxRetries: 3,
                backoff: attempt => TimeSpan.Zero,
                ct: ct);

            state = nextState;
        }

        Assert.Equal(103, state.SignalCursor);
        Assert.Equal(2, state.ProcessedCount);
        Assert.Equal(["good-1", "good-2"], state.ProcessedNotes);
        var deadLetter = Assert.Single(state.DeadLetters);
        Assert.Equal(102, deadLetter.Position);
        Assert.Contains("Poison pill detected", deadLetter.ErrorMessage);
    }
}
