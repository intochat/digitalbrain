using DigitalBrain.Core;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Ui;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

using DigitalBrain.Ui.Contracts.Ui;
using DigitalBrain.Ui.Runtime;

[GrainType("kernel.task.v1")]
public class KernelTaskNeuron(ILogger<KernelTaskNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IKernelTask
{
    public async Task HandleAsync(RunTask cmd, CancellationToken cancellationToken = default)
    {
        await FireAsync(new TaskCreated(cmd.TaskId, cmd.Description), cancellationToken);
        await FireAsync(new TaskStarted(cmd.TaskId), cancellationToken);
        await FireAsync(new TaskProgress(cmd.TaskId, "planning"), cancellationToken);
        string result;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-llm"), cancellationToken);
            var prompt = $"Perform the task and output ONLY the concise result value: {cmd.Description}";
            var response = await chat.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            result = response.Text.Trim();
            if (string.IsNullOrWhiteSpace(result)) result = "completed:" + cmd.Description;
        }
        else
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-fallback"), cancellationToken);
            result = "completed-no-llm:" + cmd.Description;
        }
        await FireAsync(new TaskProgress(cmd.TaskId, "finalizing"), cancellationToken);
        await FireAsync(new TaskCompleted(cmd.TaskId, result), cancellationToken);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, clientId: cmd.SessionId);
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value), cancellationToken);

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            await bus.BroadcastAsync(new RfwCard("digitalbrain", "TaskManagerCard", directData), cancellationToken);
        }
    }

    public async Task HandleAsync(CancelTask cmd, CancellationToken cancellationToken = default)
    {
        await FireAsync(new TaskCancelled(cmd.TaskId), cancellationToken);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, clientId: cmd.SessionId);
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value), cancellationToken);

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            await bus.BroadcastAsync(new RfwCard("digitalbrain", "TaskManagerCard", directData), cancellationToken);
        }
    }

    public Task<TaskInfo> GetInfoAsync()
    {
        var history = OutgoingJournal.Concat(IncomingJournal).ToList();
        var completed = history.OfType<TaskCompleted>().LastOrDefault();
        if (completed != null)
            return Task.FromResult(new TaskInfo(completed.TaskId, "completed", completed.Result));
        var cancelled = history.OfType<TaskCancelled>().LastOrDefault();
        if (cancelled != null)
            return Task.FromResult(new TaskInfo(cancelled.TaskId, "cancelled", null));
        var progress = history.OfType<TaskProgress>().LastOrDefault();
        if (progress != null)
            return Task.FromResult(new TaskInfo(progress.TaskId, "running:" + progress.Detail, null));
        var started = history.OfType<TaskStarted>().LastOrDefault();
        if (started != null)
            return Task.FromResult(new TaskInfo(started.TaskId, "running", null));
        var created = history.OfType<TaskCreated>().LastOrDefault();
        if (created != null)
            return Task.FromResult(new TaskInfo(created.TaskId, "created", null));
        var id = this.GetPrimaryKeyString() ?? "task";
        TaskId idTask = id;
        return Task.FromResult(new TaskInfo(idTask, "created", null));
    }
}


