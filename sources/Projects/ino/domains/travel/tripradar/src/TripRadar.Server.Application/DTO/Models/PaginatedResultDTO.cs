namespace TripRadar.Server.Application.DTO.Models;

public record PaginatedResultDTO<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
