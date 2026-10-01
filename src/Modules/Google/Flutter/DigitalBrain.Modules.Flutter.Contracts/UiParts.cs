namespace DigitalBrain.Flutter;

// A part names a piece of an app's UI; for a key it becomes the neuron "{key}/{part}".
public static class UiParts
{
    public static string NameOf(string key, string part) => key + "/" + part;
}
