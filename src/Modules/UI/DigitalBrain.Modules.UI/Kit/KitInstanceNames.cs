namespace DigitalBrain.UI;

// Kit entities (chart, image) must live under the same principal partition as the chat
// that created them, so the kernel's /kit endpoints can resolve a card back to a grain
// from the caller's principal + local name alone. Mirrors PrincipalPartition's
// "{principal:N}.{local}" scheme (see PrincipalScoped.InstanceName in DigitalBrain.Kernel).
internal static class KitInstanceNames
{
    public static string Sibling(string chatInstance, string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);

        var separator = chatInstance.IndexOf('.', StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new ArgumentException(
                $"'{chatInstance}' is not a principal-scoped instance name.", nameof(chatInstance));
        }

        return $"{chatInstance[..separator]}.{localName.Trim()}";
    }
}
