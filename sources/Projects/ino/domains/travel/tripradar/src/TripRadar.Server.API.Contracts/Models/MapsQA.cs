namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Question and answer pair
/// </summary>
public class MapsQA
{
    /// <summary>
    ///     User question
    /// </summary>
    public MapsQuestion? Question { get; set; }

    /// <summary>
    ///     Business or user answer
    /// </summary>
    public MapsAnswer? Answer { get; set; }

    /// <summary>
    ///     Total number of answers to this question
    /// </summary>
    public int? TotalAnswers { get; set; }
}
