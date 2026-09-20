namespace DigitalBrain.Supabase;

// Grain types and the ID prefix that routes live query tables to this module.
public static class SupabaseNames
{
    public const string NeuronType = "supabase";
    public const string DefaultNeuron = "default";
    public const string TableType = "supabase-table";
    public const string TableIdPrefix = "sbtable-";
}