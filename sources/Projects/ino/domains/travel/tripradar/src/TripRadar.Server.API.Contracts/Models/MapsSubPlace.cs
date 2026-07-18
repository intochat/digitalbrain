namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Business within a location
/// </summary>
public class MapsSubPlace
{
    /// <summary>
    ///     Position in list
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    ///     Business name
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
    ///     Number of reviews
    /// </summary>
    public int? Reviews { get; set; }

    /// <summary>
    ///     Business type
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     Business type ID
    /// </summary>
    public string? TypeId { get; set; }

    /// <summary>
    ///     Address
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    ///     Specific location within building
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    ///     Open/closed status
    /// </summary>
    public string? OpenState { get; set; }

    /// <summary>
    ///     Operating hours as a human-readable string (e.g., "Ouvert ⋅ Ferme à 18:00")
    /// </summary>
    public string? Hours { get; set; }

    /// <summary>
    ///     Thumbnail image
    /// </summary>
    public string? Thumbnail { get; set; }
}
