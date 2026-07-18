using System.Threading.Tasks;

namespace DigitalBrain.Abstractions.Tasks;

public interface IDurableTaskHandler
{
    string TaskType { get; }
    Task<string> ExecuteAsync(string taskId);
}
