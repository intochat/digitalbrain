using Brain.Contracts;

namespace Brain.Modules.Web;

public interface IWeb : INeuronContract
{
    static string ContractDescription => "Owner-scoped bounded HTTP fetch neuron.";
    [NeuronContract("web.fetch.v1")]
    Task<WebReply> FetchAsync(WebFetch request);
}

public sealed record WebFetch(string Url, int? MaxBytes = null);
public sealed record WebReply(int Status, string Body, long Revision);
