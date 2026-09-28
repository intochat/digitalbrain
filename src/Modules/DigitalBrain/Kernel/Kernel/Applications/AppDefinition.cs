namespace DigitalBrain.Core;

public sealed record AppDefinition(string Name, IReadOnlyList<Type> RequiredModules, IReadOnlyList<Func<ApplicationStart, Task>> Starts)
{
    public static string NameOf<TApp>() where TApp : IApplication => typeof(TApp).FullName!;

    public static AppDefinition Of<TApp>() where TApp : IApplication, new()
    {
        var builder = new Builder();
        new TApp().Configure(builder);
        return new(NameOf<TApp>(), builder.Modules, builder.Starts);
    }

    private sealed class Builder : IAppBuilder
    {
        public List<Type> Modules { get; } = [];
        public List<Func<ApplicationStart, Task>> Starts { get; } = [];

        public IAppBuilder RequireModule<TModule>() where TModule : class, IModule, new()
        {
            if (!Modules.Contains(typeof(TModule))) { Modules.Add(typeof(TModule)); }
            return this;
        }

        public IAppBuilder OnStart(Func<ApplicationStart, Task> start)
        {
            ArgumentNullException.ThrowIfNull(start);
            Starts.Add(start);
            return this;
        }
    }
}
