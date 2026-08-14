namespace Brain.Runtime.Abstractions;

public interface IProductActivityGrain : IGrainWithGuidKey
{
    Task<RuntimeActivityReceipt> StartAsync(RuntimeInvocation invocation);

    Task<RuntimeActivitySnapshot?> GetAsync(string workspace);
}
