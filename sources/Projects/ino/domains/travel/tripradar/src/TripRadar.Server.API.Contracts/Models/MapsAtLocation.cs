namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Place at this location
/// </summary>
public class MapsAtLocation
{
    /// <summary>
    ///     Position in list
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    ///     Place name
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Data ID
    /// </summary>
    public string? DataId { get; set; }

    /// <summary>
    ///     CID
    /// </summary>
    public string? DataCid { get; set; }

    /// <summary>
    ///     Reviews link
    /// </summary>
    public string? ReviewsLink { get; set; }

    /// <summary>
    ///     Photos link
    /// </summary>
    public string? PhotosLink { get; set; }

    /// <summary>
    ///     GPS coordinates
    /// </summary>
    public GpsCoordinates? GpsCoordinates { get; set; }

    /// <summary>
    ///     Place ID search link
    /// </summary>
    public string? PlaceIdSearch { get; set; }

    /// <summary>
    ///     Rating
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    ///     Place type
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     Price level
    /// </summary>
    public string? Price { get; set; }

    /// <summary>
    ///     Thumbnail image
    /// </summary>
    public string? Thumbnail { get; set; }
}
