namespace TripRadar.Server.Application.DTO.Responses;

public sealed record AirlineResponseDTO(string AirlineCode, string AirlineName, bool IsAlliance, string? LogoUrl);
