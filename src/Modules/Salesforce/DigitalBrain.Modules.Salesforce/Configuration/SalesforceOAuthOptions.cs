namespace DigitalBrain.Salesforce;

// Contains credentials. Do not serialize or log this options instance.
public sealed class SalesforceOAuthOptions
{
    public const string SectionName = "DigitalBrain:Salesforce:OAuth";
    public string ConsumerKey { get; set; } = "";
    public string ConsumerSecret { get; set; } = "";
    public string PublicOrigin { get; set; } = "";
}
