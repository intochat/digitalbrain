using AutoMapper;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;
using ApiQueryColumn = TripRadar.Server.API.Contracts.Models.QueryColumn;
using QueryColumn = TripRadar.Server.Domain.ValueObjects.QueryColumn;

namespace TripRadar.Server.API.Mappings;

internal sealed class ScheduledExecutionProfile : Profile
{
    public ScheduledExecutionProfile()
    {
        CreateMap<UpdateScheduledExecutionConfigurationRequest, UpdateScheduledExecutionConfigurationCommand>();

        CreateMap<QueryColumn, ApiQueryColumn>();

        CreateMap<ScheduledExecutionDetails, ScheduledExecutionItem>();
    }
}
