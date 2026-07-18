namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Admission and ticket information
/// </summary>
public class MapsAdmission
{
    /// <summary>
    ///     Source name
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    ///     Source icon
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    ///     Available ticket options
    /// </summary>
    public List<MapsAdmissionOption>? Options { get; set; }
}
