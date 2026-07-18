namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Place features and highlights
/// </summary>
public class MapsExtension
{
    /// <summary>
    ///     Featured highlights (e.g., "Fast service", "Great coffee")
    /// </summary>
    public List<string>? Highlights { get; set; }

    /// <summary>
    ///     Popular for activities (e.g., "Breakfast", "Solo dining")
    /// </summary>
    public List<string>? PopularFor { get; set; }

    /// <summary>
    ///     Accessibility features
    /// </summary>
    public List<string>? Accessibility { get; set; }

    /// <summary>
    ///     Crowd information
    /// </summary>
    public List<string>? Crowd { get; set; }

    /// <summary>
    ///     Payment methods accepted
    /// </summary>
    public List<string>? Payments { get; set; }

    /// <summary>
    ///     Planning features (reservations, etc.)
    /// </summary>
    public List<string>? Planning { get; set; }
}
