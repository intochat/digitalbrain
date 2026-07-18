using AutoMapper;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Entities;
using FeedbackCategory = TripRadar.Server.API.Contracts.Models.FeedbackCategory;

namespace TripRadar.Server.API.Mappings;

internal sealed class FeedbackProfile : Profile
{
    public FeedbackProfile()
    {
        CreateMap<Feedback, GetUserFeedbackResponse>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name));

        CreateMap<Feedback, GetAllFeedbacksResponse>()
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.User.Profile.Username))
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Name));

        CreateMap<(CreateFeedbackRequest Request, FeedbackCategoryType FeedbackCategoryType, DateTime CreatedOn), GetUserFeedbackResponse>()
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Request.Title))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Request.Content))
            .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Request.Rating))
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.FeedbackCategoryType.Name))
            .ForMember(dest => dest.CreatedOn, opt => opt.MapFrom(src => src.CreatedOn));

        CreateMap<PaginatedResultDTO<Feedback>, PaginatedResponse<GetAllFeedbacksResponse>>()
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items));

        CreateMap<Domain.ReferenceData.FeedbackCategory, FeedbackCategory>();

        CreateMap<GetFeedbackResponseDTO, GetFeedbackResponse>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content))
            .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => src.Rating))
            .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
            .ForMember(dest => dest.CreatedOn, opt => opt.MapFrom(src => src.CreatedOn))
            .ForMember(dest => dest.UpdatedOn, opt => opt.MapFrom(src => src.UpdatedOn));
    }
}
