namespace Brain.Gateway;

public interface ISurfaceOwnerResolver
{
    ISurfaceOwner Resolve(string contractId, string instanceId);
}
