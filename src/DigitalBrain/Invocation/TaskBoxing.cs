using System.Collections.Concurrent;
using System.Reflection;

namespace DigitalBrain.Invocation;

// Awaits a task known only as a Task and reads its result without reflecting over Result on every call.
internal static class TaskBoxing
{
    private static readonly ConcurrentDictionary<Type, Func<Task, Task<object?>>> Boxes = new();
    private static readonly MethodInfo BoxMethod = typeof(TaskBoxing).GetMethod(nameof(Box), BindingFlags.NonPublic | BindingFlags.Static)!;

    // resultType is the task's result type, or null for a task without one.
    public static async Task<object?> ResultAsync(Task task, Type? resultType)
    {
        if (resultType is null) { await task.ConfigureAwait(true); return null; }
        var box = Boxes.GetOrAdd(resultType, static result => BoxMethod.MakeGenericMethod(result).CreateDelegate<Func<Task, Task<object?>>>());
        return await box(task).ConfigureAwait(true);
    }

    private static async Task<object?> Box<T>(Task task) => await ((Task<T>)task).ConfigureAwait(true);
}
