namespace TripRadar.Server.API.Contracts.Models;

public class GeographicLocation
{
    /// <summary>
    ///     Parameter defines from where you want the search to originate.
    ///     It is recommended to specify location at the city level.
    ///     Cannot be used together with Uule parameter.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    ///     Parameter is the Google encoded location you want to use for the search.
    ///     Cannot be used together with Location parameter.
    /// </summary>
    public string? Uule { get; set; }

    /// <summary>
    ///     Parameter defines the id (CID) of the Google My Business listing you want to scrape.
    ///     Also known as Google Place ID.
    /// </summary>
    public string? Ludocid { get; set; }
}
