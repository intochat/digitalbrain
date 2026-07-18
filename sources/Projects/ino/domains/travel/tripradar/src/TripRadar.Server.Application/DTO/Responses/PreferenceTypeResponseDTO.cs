namespace TripRadar.Server.Application.DTO.Responses;

public sealed record PreferenceTypeResponseDTO(
    string ServiceTypeName,
    string Name,
    string DataType,
    string? ValidationSchema,
    bool IsRequired,
    string? DefaultValue);
