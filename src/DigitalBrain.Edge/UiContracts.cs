using System.Text.Json.Serialization;
using DigitalBrain.Product.Presentation;

namespace DigitalBrain.Edge;

/// <summary>
/// An Edge-issued reference that carries no product binding or workspace identity.
/// </summary>
public readonly record struct OpaqueUiActionReference
{
    [JsonConstructor]
    public OpaqueUiActionReference(string? value)
    {
        Value = value ?? string.Empty;
    }

    public string Value { get; }
}

/// <summary>
/// The result of asking Edge to submit an opaque UI action.
/// </summary>
public sealed record UiActionReceipt(bool Accepted);

/// <summary>
/// A fixed semantic UI surface produced from trusted presentation facts.
/// </summary>
public sealed record UiSurface
{
    public UiSurface(
        string surfaceId,
        long revision,
        IReadOnlyList<BaseUiKitComponent> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(components);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "A UI surface needs a positive revision.");
        }

        var copy = components.ToArray();
        if (copy.Any(static component => component is null))
        {
            throw new ArgumentException("A UI surface cannot contain null components.", nameof(components));
        }

        SurfaceId = surfaceId.Trim();
        Revision = revision;
        Components = Array.AsReadOnly(copy);
    }

    public string SurfaceId { get; }

    public long Revision { get; }

    public IReadOnlyList<BaseUiKitComponent> Components { get; }
}

/// <summary>
/// A point-in-time observation of the UI surfaces visible through one channel.
/// The revision is the maximum revision observed while assembling the snapshot;
/// it is not a global journal cursor.
/// </summary>
public sealed record UiWorkspaceSnapshot
{
    public UiWorkspaceSnapshot(long revision, IReadOnlyList<UiSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "A workspace observation revision cannot be negative.");
        }

        var copy = surfaces.ToArray();
        if (copy.Any(static surface => surface is null))
        {
            throw new ArgumentException("A workspace snapshot cannot contain null surfaces.", nameof(surfaces));
        }

        Revision = revision;
        Surfaces = Array.AsReadOnly(copy);
    }

    public long Revision { get; }

    public IReadOnlyList<UiSurface> Surfaces { get; }
}

/// <summary>
/// Reads trusted, renderer-neutral presentation facts for the already-bound workspace.
/// </summary>
public interface IWorkspaceUiSurfaceSource
{
    Task<ApprovalWorkspaceSurfaceRequested?> ReadApprovalsAsync(CancellationToken cancellationToken);

    Task<UiSurface?> ReadSalesAsync(string queryId, CancellationToken cancellationToken);
}

/// <summary>
/// Authorizes an opaque action after Edge has resolved it against the current safe snapshot.
/// </summary>
public interface IUiActionAuthorizer
{
    Task<bool> AuthorizeAsync(OpaqueUiActionReference action, CancellationToken cancellationToken);
}

/// <summary>
/// Submits a valid approval action through the caller's source-bound channel.
/// </summary>
public interface IApprovalUiActionBridge
{
    Task<UiActionReceipt> InvokeAsync(OpaqueUiActionReference action, CancellationToken cancellationToken);
}

/// <summary>
/// The closed Base UI Kit component contract. Undeclared component discriminators are not accepted.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "component")]
[JsonDerivedType(typeof(ChatComponent), typeDiscriminator: "Chat")]
[JsonDerivedType(typeof(InboxComponent), typeDiscriminator: "Inbox")]
[JsonDerivedType(typeof(DrawerComponent), typeDiscriminator: "Drawer")]
[JsonDerivedType(typeof(CardComponent), typeDiscriminator: "Card")]
[JsonDerivedType(typeof(StatusComponent), typeDiscriminator: "Status")]
[JsonDerivedType(typeof(EvidenceComponent), typeDiscriminator: "Evidence")]
[JsonDerivedType(typeof(ChangesComponent), typeDiscriminator: "Changes")]
[JsonDerivedType(typeof(ActionComponent), typeDiscriminator: "Action")]
[JsonDerivedType(typeof(BarChartComponent), typeDiscriminator: "BarChart")]
[JsonDerivedType(typeof(TableComponent), typeDiscriminator: "Table")]
[JsonDerivedType(typeof(UnavailableComponent), typeDiscriminator: "Unavailable")]
public abstract record BaseUiKitComponent;

public sealed record ChatComponent : BaseUiKitComponent
{
    public ChatComponent(string route, string cardId)
    {
        Route = UiContractValidation.Required(route, nameof(route));
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
    }

    public string Route { get; }

    public string CardId { get; }
}

public sealed record InboxComponent : BaseUiKitComponent
{
    public InboxComponent(string title, IReadOnlyList<string> cardIds)
    {
        Title = UiContractValidation.Required(title, nameof(title));
        CardIds = UiContractValidation.RequiredStrings(cardIds, nameof(cardIds));
    }

    public string Title { get; }

    public IReadOnlyList<string> CardIds { get; }
}

public sealed record DrawerComponent : BaseUiKitComponent
{
    public DrawerComponent(string route, string cardId)
    {
        Route = UiContractValidation.Required(route, nameof(route));
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
    }

    public string Route { get; }

    public string CardId { get; }
}

public sealed record CardComponent : BaseUiKitComponent
{
    public CardComponent(string cardId, string title, string summary)
    {
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
        Title = UiContractValidation.Required(title, nameof(title));
        Summary = UiContractValidation.Required(summary, nameof(summary));
    }

    public string CardId { get; }

    public string Title { get; }

    public string Summary { get; }
}

public sealed record StatusComponent : BaseUiKitComponent
{
    public StatusComponent(string cardId, string value)
    {
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
        Value = UiContractValidation.Required(value, nameof(value));
    }

    public string CardId { get; }

    public string Value { get; }
}

public sealed record EvidenceComponent : BaseUiKitComponent
{
    public EvidenceComponent(string cardId, IReadOnlyList<UiEvidence> items)
    {
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
        Items = UiContractValidation.RequiredValues(items, nameof(items));
    }

    public string CardId { get; }

    public IReadOnlyList<UiEvidence> Items { get; }
}

public sealed record ChangesComponent : BaseUiKitComponent
{
    public ChangesComponent(string cardId, IReadOnlyList<UiChange> items)
    {
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
        Items = UiContractValidation.RequiredValues(items, nameof(items));
    }

    public string CardId { get; }

    public IReadOnlyList<UiChange> Items { get; }
}

public sealed record ActionComponent : BaseUiKitComponent
{
    public ActionComponent(string cardId, string label, OpaqueUiActionReference action)
    {
        CardId = UiContractValidation.Required(cardId, nameof(cardId));
        Label = UiContractValidation.Required(label, nameof(label));
        if (string.IsNullOrWhiteSpace(action.Value))
        {
            throw new ArgumentException("A UI action needs an opaque reference.", nameof(action));
        }

        Action = action;
    }

    public string CardId { get; }

    public string Label { get; }

    public OpaqueUiActionReference Action { get; }
}

public sealed record BarChartComponent : BaseUiKitComponent
{
    public BarChartComponent(string title, string currencyCode, IReadOnlyList<UiChartPoint> points)
    {
        Title = UiContractValidation.Required(title, nameof(title));
        CurrencyCode = UiContractValidation.Currency(currencyCode, nameof(currencyCode));
        Points = UiContractValidation.RequiredValues(points, nameof(points));
    }

    public string Title { get; }

    public string CurrencyCode { get; }

    public IReadOnlyList<UiChartPoint> Points { get; }
}

public sealed record TableComponent : BaseUiKitComponent
{
    public TableComponent(
        string title,
        string currencyCode,
        IReadOnlyList<UiTableRow> rows,
        decimal totalAmount,
        int closedDealCount)
    {
        if (totalAmount < 0 || closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "A table aggregate cannot be negative.");
        }

        Title = UiContractValidation.Required(title, nameof(title));
        CurrencyCode = UiContractValidation.Currency(currencyCode, nameof(currencyCode));
        Rows = UiContractValidation.RequiredValues(rows, nameof(rows));
        TotalAmount = totalAmount;
        ClosedDealCount = closedDealCount;
    }

    public string Title { get; }

    public string CurrencyCode { get; }

    public IReadOnlyList<UiTableRow> Rows { get; }

    public decimal TotalAmount { get; }

    public int ClosedDealCount { get; }
}

public sealed record UnavailableComponent : BaseUiKitComponent
{
    public UnavailableComponent(string title, string reason)
    {
        Title = UiContractValidation.Required(title, nameof(title));
        Reason = UiContractValidation.Required(reason, nameof(reason));
    }

    public string Title { get; }

    public string Reason { get; }
}

public sealed record UiEvidence
{
    public UiEvidence(string source, string summary, string? reference)
    {
        Source = UiContractValidation.Required(source, nameof(source));
        Summary = UiContractValidation.Required(summary, nameof(summary));
        Reference = reference?.Trim();
    }

    public string Source { get; }

    public string Summary { get; }

    public string? Reference { get; }
}

public sealed record UiChange
{
    public UiChange(string field, string? before, string after)
    {
        Field = UiContractValidation.Required(field, nameof(field));
        Before = before;
        After = UiContractValidation.Required(after, nameof(after));
    }

    public string Field { get; }

    public string? Before { get; }

    public string After { get; }
}

public sealed record UiChartPoint
{
    public UiChartPoint(DateOnly date, decimal amount, int closedDealCount)
    {
        if (date == default || amount < 0 || closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "A chart point needs safe non-negative aggregate values.");
        }

        Date = date;
        Amount = amount;
        ClosedDealCount = closedDealCount;
    }

    public DateOnly Date { get; }

    public decimal Amount { get; }

    public int ClosedDealCount { get; }
}

public sealed record UiTableRow
{
    public UiTableRow(DateOnly date, decimal amount, int closedDealCount)
    {
        if (date == default || amount < 0 || closedDealCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "A table row needs safe non-negative aggregate values.");
        }

        Date = date;
        Amount = amount;
        ClosedDealCount = closedDealCount;
    }

    public DateOnly Date { get; }

    public decimal Amount { get; }

    public int ClosedDealCount { get; }
}

internal static class UiContractValidation
{
    internal static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    internal static IReadOnlyList<string> RequiredStrings(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.Select(value => Required(value, parameterName)).ToArray();
        return Array.AsReadOnly(copy);
    }

    internal static IReadOnlyList<T> RequiredValues<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("A component cannot contain null values.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }

    internal static string Currency(string currencyCode, string parameterName)
    {
        var normalized = Required(currencyCode, parameterName).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("A currency code must contain three ASCII letters.", parameterName);
        }

        return normalized;
    }
}
