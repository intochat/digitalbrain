using System.Globalization;
using Brain.Abstractions.Operations;
using Brain.Product.Abstractions.Operations;

namespace DigitalBrain.ProductHost.Catalog;

public sealed class ProductOperationRegistration
{
    public ProductOperationRegistration(
        OperationDescriptor declaredOperation,
        ProductOperationBinding adapter,
        ProductOperationAccessPolicy accessPolicy)
    {
        ArgumentNullException.ThrowIfNull(declaredOperation);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(accessPolicy);

        Identity = ProductOperationIdentity.Parse(declaredOperation);
        var productDescriptor = adapter.Descriptor;
        if (productDescriptor.Operation != declaredOperation)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation '{declaredOperation.Id}' does not match its adapter descriptor.");
        }

        DeclaredOperation = declaredOperation;
        Adapter = adapter;
        Descriptor = productDescriptor;
        AccessPolicy = accessPolicy;
    }

    public OperationDescriptor DeclaredOperation { get; }

    public ProductOperationBinding Adapter { get; }

    public ProductOperationDescriptor Descriptor { get; }

    public ProductOperationAccessPolicy AccessPolicy { get; }

    internal ProductOperationIdentity Identity { get; }

}

internal sealed record ProductOperationIdentity(string Module, string Name, int Major)
{
    internal string NorthboundIdentity => $"{Module}.{Name.Replace('-', '_')}";

    internal static ProductOperationIdentity Parse(OperationDescriptor operation)
    {
        var value = operation.Id.Value;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        var at = value.LastIndexOf('@');
        if (slash <= 0
            || at <= slash + 1
            || at == value.Length - 1
            || value.IndexOf('/', slash + 1) >= 0
            || value.IndexOf('@') != at)
        {
            throw Invalid(operation, "must use canonical 'module/name@major' syntax");
        }

        var module = value[..slash];
        var name = value[(slash + 1)..at];
        var majorText = value[(at + 1)..];
        if (!IsCanonicalSegment(module) || !IsCanonicalSegment(name))
        {
            throw Invalid(operation, "must use lowercase ASCII module and name segments");
        }

        if (!string.Equals(module, operation.Owner.Value, StringComparison.Ordinal))
        {
            throw Invalid(operation, "must use its declaring module owner as the id prefix");
        }

        if (!int.TryParse(majorText, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || major <= 0
            || major != operation.Version.Major
            || !string.Equals(majorText, major.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw Invalid(operation, "must use its descriptor major version as the canonical suffix");
        }

        return new ProductOperationIdentity(module, name, major);
    }

    private static bool IsCanonicalSegment(string value)
        => value.Length > 0
            && IsLowerAsciiLetter(value[0])
            && IsLowerAsciiLetterOrDigit(value[^1])
            && value.All(static character =>
                IsLowerAsciiLetterOrDigit(character) || character == '-');

    private static bool IsLowerAsciiLetter(char value) => value is >= 'a' and <= 'z';

    private static bool IsLowerAsciiLetterOrDigit(char value)
        => IsLowerAsciiLetter(value) || value is >= '0' and <= '9';

    private static ProductOperationCatalogConfigurationException Invalid(
        OperationDescriptor operation,
        string requirement)
        => new($"Product operation '{operation.Id}' {requirement}.");
}

public sealed class ProductOperationCatalogConfigurationException : Exception
{
    public ProductOperationCatalogConfigurationException(string message)
        : base(message)
    {
    }

    public ProductOperationCatalogConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
