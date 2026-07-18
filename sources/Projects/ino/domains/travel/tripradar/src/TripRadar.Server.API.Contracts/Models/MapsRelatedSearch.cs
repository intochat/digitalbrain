namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Related search suggestions
/// </summary>
public class MapsRelatedSearch
{
    /// <summary>
    ///     Search term that people also use
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    ///     Local results for this search term
    /// </summary>
    public List<LocalPlaceResult>? LocalResults { get; set; }
}
