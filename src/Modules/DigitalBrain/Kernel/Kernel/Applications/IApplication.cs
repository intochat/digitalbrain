namespace DigitalBrain.Core;

// An application composes modules the way a host does and says what happens when it is started for a key.
public interface IApplication
{
    void Configure(IAppBuilder app);
}

public interface IAppBuilder
{
    IAppBuilder RequireModule<TModule>() where TModule : class, IModule, new();
    IAppBuilder OnStart(Func<ApplicationStart, Task> start);
}

public sealed record ApplicationStart(string Key, IGrainFactory Grains);
