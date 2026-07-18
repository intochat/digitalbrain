namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     User question
/// </summary>
public class MapsQuestion
{
    /// <summary>
    ///     User who asked the question
    /// </summary>
    public MapsUser? User { get; set; }

    /// <summary>
    ///     Question text
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    ///     When question was asked
    /// </summary>
    public string? Date { get; set; }

    /// <summary>
    ///     Language of the question
    /// </summary>
    public string? Language { get; set; }
}
