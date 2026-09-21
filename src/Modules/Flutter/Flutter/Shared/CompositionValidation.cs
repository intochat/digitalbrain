namespace DigitalBrain.Flutter;

internal static class CompositionValidation
{
    public static void Children(IReadOnlyList<UiChildRef> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count > 128) { throw new ArgumentException("A node supports at most 128 children."); }
        foreach (var child in children)
        {
            if (child is not null && child.Kind is not ("surface" or "layout" or "collection" or "imagecanvas" or "card" or "button" or "textfield" or "tabs" or "text")) { throw new ArgumentException("Unsupported composition child kind."); }
            if (child is null || string.IsNullOrWhiteSpace(child.Kind) || string.IsNullOrWhiteSpace(child.Name) || child.Name.Length > 512) { throw new ArgumentException("Children need a kind and stable name."); }
        }
        if (children.Distinct().Count() != children.Count) { throw new ArgumentException("Child references must be unique."); }
    }
}