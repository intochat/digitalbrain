namespace DigitalBrain.Supabase.Aspire.Hosting;

public sealed class SupabaseHostingOptions
{
    public string ParameterName { get; set; } = "Supabase";
    public string ConnectionName { get; set; } = SupabaseModule.ConnectionName;
}