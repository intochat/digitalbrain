using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using IntoChat.Apps;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// Turns the workspace's open form and live-table windows into a saved declarative app, and reopens
// that app's windows on request.
internal sealed class WorkspaceAppTools(WorkspaceAppService apps) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> SaveAsApp(
            [Description("A short name for the saved app.")] string name,
            CancellationToken ct)
        {
            var trusted = context();
            try { return Handle(await apps.SaveAsync(trusted.ScopeId, name, ct)); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        async Task<object> OpenApp(
            [Description("The app id returned by save_as_app.")] string appId,
            CancellationToken ct)
        {
            var trusted = context();
            try
            {
                var state = await apps.OpenAsync(trusted.ScopeId, appId, ct);
                return new { appId, windows = state.Windows.Select(window => new { id = window.Id, title = window.Title, kind = window.Reference.Kind }).ToArray() };
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        return
        [
            AIFunctionFactory.Create(SaveAsApp, "save_as_app", "Save the workspace's open form and live-table windows as a reusable app. Returns the app id and version."),
            AIFunctionFactory.Create(OpenApp, "open_app", "Reopen the windows of a saved app in the workspace. Returns the open windows."),
        ];
    }

    private static object Handle(AppInstallation installation) => new
    {
        appId = installation.Manifest.Id,
        name = installation.Manifest.Name,
        version = installation.Manifest.Version,
        windows = installation.Manifest.Windows.Select(window => new { id = window.WindowId, title = window.Title, kind = window.Kind }).ToArray(),
    };

    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}
