namespace DigitalBrain.Capabilities.Storage;

/// <summary>How a listing is sorted.</summary>
public enum SortKey
{
    /// <summary>Folders first, then by name.</summary>
    FolderThenName,

    /// <summary>By name.</summary>
    Name,

    /// <summary>By modification time.</summary>
    Modified,

    /// <summary>By size.</summary>
    Size,
}

/// <summary>How a search matches. Only <see cref="Name"/> exists in 0.7.0; content search may follow.</summary>
public enum SearchMode
{
    /// <summary>Matches item names.</summary>
    Name,
}

/// <summary>A page request for <see cref="IStorage.List"/>.</summary>
/// <param name="Cursor">The cursor from the previous page, or null for the first page.</param>
/// <param name="PageSize">The page size, clamped to 1..1000.</param>
/// <param name="Sort">The sort key.</param>
/// <param name="Descending">True to reverse the order.</param>
public sealed record ListQuery(string? Cursor = null, int PageSize = 100, SortKey Sort = SortKey.FolderThenName, bool Descending = false)
{
    /// <summary>The largest page a storage returns.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>The page size, clamped to 1..<see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; init => field = Clamp(value); } = Clamp(PageSize);

    /// <summary>The sort key; undefined values are rejected.</summary>
    public SortKey Sort
    {
        get;
        init => field = Defined(value);
    } = Defined(Sort);

    internal static int Clamp(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    internal static T Defined<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Undefined value.");
}

/// <summary>A search request for <see cref="IStorage.Search"/>.</summary>
/// <param name="Text">The text to match; not blank.</param>
/// <param name="Mode">How to match.</param>
/// <param name="Within">Limit to this folder and below, or null for the whole storage.</param>
/// <param name="Cursor">The cursor from the previous page.</param>
/// <param name="PageSize">The page size, clamped to 1..1000.</param>
public sealed record SearchQuery(string Text, SearchMode Mode = SearchMode.Name, ItemId? Within = null, string? Cursor = null, int PageSize = 100)
{
    /// <summary>The text to match; not blank.</summary>
    public string Text
    {
        get;
        init => field = NotBlank(value);
    } = NotBlank(Text);

    /// <summary>How to match; undefined values are rejected.</summary>
    public SearchMode Mode
    {
        get;
        init => field = ListQuery.Defined(value);
    } = ListQuery.Defined(Mode);

    /// <summary>The page size, clamped to 1..<see cref="ListQuery.MaxPageSize"/>.</summary>
    public int PageSize { get; init => field = ListQuery.Clamp(value); } = ListQuery.Clamp(PageSize);

    private static string NotBlank(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text;
    }
}
