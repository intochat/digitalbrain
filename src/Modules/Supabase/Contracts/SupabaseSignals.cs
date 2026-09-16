namespace DigitalBrain.Supabase;

// Work a command schedules for its own reaction; these never travel along a synapse.
// The outward card signal is UIVocabulary.TableRendered with the body { "name": "...", "title": "..." }.
public static class SupabaseSignals
{
    public const string QueryTableCreating = "QueryTableCreating";
    public const string QueryTableUpdating = "QueryTableUpdating";
}
