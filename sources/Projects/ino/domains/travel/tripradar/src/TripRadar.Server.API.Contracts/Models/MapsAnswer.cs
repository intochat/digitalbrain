namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Answer to a question
/// </summary>
public class MapsAnswer
{
    /// <summary>
    ///     User who provided the answer
    /// </summary>
    public MapsUser? User { get; set; }

    /// <summary>
    ///     Answer text
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    ///     When answer was provided
    /// </summary>
    public string? Date { get; set; }

    /// <summary>
    ///     Language of the answer
    /// </summary>
    public string? Language { get; set; }
}
