using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Ino.Aspire.Hosting;

public static class InoSiloEnvironmentExtensions
{
    public static IResourceBuilder<T> PropagateInoConfig<T>(
        this IResourceBuilder<T> resource,
        IInoBuilder ino)
        where T : IResourceWithEnvironment
    {
        for (var i = 0; i < ino.DeclaredModels.Count; i++)
        {
            var b = ino.DeclaredModels[i];
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Provider", b.Model.Provider);
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Id", b.Model.Id);
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Tier", b.Tier.ToString());
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Type",
                $"{b.Model.GetType().FullName}, {b.Model.GetType().Assembly.GetName().Name}");
        }

        // Bind each declared provider's secret API key (Aspire prompts for it
        // in the dashboard on first run) into the silo's IConfiguration as
        // Ino:Llm:ApiKeys:<provider>. The silo-side AddInoChatClients reads
        // it from there, not from process env vars — so a clean machine boots
        // straight from the dashboard prompt.
        foreach (var (provider, param) in ino.ApiKeyParameters)
            resource.WithEnvironment($"Ino__Llm__ApiKeys__{provider}", param);

        if (ino.DeclaredVoiceProvider is { } voice)
            resource.WithEnvironment("Ino__Voice__Provider", voice.Name);

        return resource;
    }
}
