using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;

namespace Brain.Core.Modules;

public sealed class ManifestValidationException(string message) : InvalidOperationException(message);

public static class ManifestValidator
{
    public static ModuleSet Validate(IReadOnlyCollection<ModuleManifest> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        var modules = Index(installed, static manifest => manifest.Id.Value, "module");
        var roles = Index(installed.SelectMany(static manifest => manifest.Roles), static role => role.Id.Value, "role");
        var operations = Index(installed.SelectMany(static manifest => manifest.Operations), static operation => operation.Id.Value, "operation");
        var events = Index(installed.SelectMany(static manifest => manifest.Events), static @event => @event.Contract.Value, "event");
        var capabilities = Index(installed.SelectMany(static manifest => manifest.ProvidedCapabilities), static capability => capability.Id.Value, "provided capability");

        ValidateDependencies(installed, modules);
        ValidateOwners(installed, roles);
        ValidateEventConsumers(installed, events);
        ValidateReshapes(installed, events);

        return new ModuleSet(
            installed.OrderBy(static manifest => manifest.Id.Value, StringComparer.Ordinal).ToArray(),
            modules,
            operations,
            events,
            capabilities);
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> declarations, Func<T, string> id, string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var key = id(declaration);
            if (!result.TryAdd(key, declaration))
            {
                throw new ManifestValidationException($"Duplicate {kind} id '{key}'.");
            }
        }

        return result;
    }

    private static void ValidateDependencies(
        IEnumerable<ModuleManifest> manifests,
        IReadOnlyDictionary<string, ModuleManifest> modules)
    {
        foreach (var manifest in manifests)
        {
            foreach (var dependency in manifest.Dependencies)
            {
                if (!modules.TryGetValue(dependency.Module.Value, out var target))
                {
                    throw new ManifestValidationException(
                        $"Module '{manifest.Id}' requires missing dependency '{dependency.Module}'.");
                }

                if (!dependency.Accepts(target.Version))
                {
                    throw new ManifestValidationException(
                        $"Module '{manifest.Id}' requires compatible version {dependency.MinimumInclusive}..{dependency.MaximumExclusive} of '{dependency.Module}', but '{target.Version}' is installed.");
                }
            }
        }
    }

    private static void ValidateOwners(
        IEnumerable<ModuleManifest> manifests,
        IReadOnlyDictionary<string, RoleDescriptor> roles)
    {
        foreach (var manifest in manifests)
        {
            foreach (var role in manifest.Roles)
            {
                RequireOwner("role", role.Id.Value, role.Owner, manifest);
            }

            foreach (var operation in manifest.Operations)
            {
                RequireOwner("operation", operation.Id.Value, operation.Owner, manifest);
                if (!roles.TryGetValue(operation.EntryRole.Value, out var role)
                    || role.Owner != manifest.Id)
                {
                    throw new ManifestValidationException(
                        $"Operation '{operation.Id}' in module '{manifest.Id}' declares entry role '{operation.EntryRole}' that is absent from its owning manifest.");
                }
            }

            foreach (var @event in manifest.Events)
            {
                RequireOwner("event", @event.Contract.Value, @event.Owner, manifest);
            }

            foreach (var capability in manifest.ProvidedCapabilities)
            {
                RequireOwner("capability", capability.Id.Value, capability.Owner, manifest);
            }
        }
    }

    private static void RequireOwner(string kind, string id, ModuleId owner, ModuleManifest manifest)
    {
        if (owner != manifest.Id)
        {
            throw new ManifestValidationException(
                $"{kind} '{id}' is owned by '{owner}', not manifest '{manifest.Id}'.");
        }
    }

    private static void ValidateEventConsumers(
        IEnumerable<ModuleManifest> manifests,
        IReadOnlyDictionary<string, EventDescriptor> events)
    {
        foreach (var manifest in manifests)
        {
            foreach (var contract in manifest.ConsumedEvents)
            {
                if (!events.TryGetValue(contract.Value, out var @event))
                {
                    throw new ManifestValidationException(
                        $"Module '{manifest.Id}' consumes undeclared event '{contract}'.");
                }

                if (@event.Owner != manifest.Id && @event.Visibility != EventVisibility.Published)
                {
                    throw new ManifestValidationException(
                        $"Module '{manifest.Id}' consumes internal event '{contract}' owned by '{@event.Owner}'.");
                }
            }
        }
    }

    private static void ValidateReshapes(
        IEnumerable<ModuleManifest> manifests,
        IReadOnlyDictionary<string, EventDescriptor> events)
    {
        foreach (var manifest in manifests)
        {
            foreach (var reshape in manifest.Reshapes)
            {
                RequireOwner("reshape", reshape.InputEvent.Value, reshape.Owner, manifest);
                if (!events.TryGetValue(reshape.InputEvent.Value, out var input)
                    || !events.TryGetValue(reshape.OutputEvent.Value, out var output)
                    || input.Owner != manifest.Id
                    || output.Owner != manifest.Id)
                {
                    throw new ManifestValidationException(
                        $"Reshape in module '{manifest.Id}' requires input and output events declared by its owning manifest.");
                }
            }
        }
    }
}
