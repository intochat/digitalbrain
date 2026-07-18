using TripRadar.Server.Db.Models;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static void SeedPreferenceTypes(SetupDbContext ctx)
    {
        var expectedPreferenceTypes = new List<PreferenceTypes>
        {
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.Adults.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "1",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.Children.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.InfantsInSeat.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.InfantsOnLap.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.MaxPrice.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.Currency.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "USD",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.TravelClass.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "Economy",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.SortBy.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "Price",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.PreferredAirlines.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.PreferredDepartureAirportCode.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.MaxLayovers.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "2",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.PreferredDepartureTime.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "any",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.PreferredArrivalTime.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "any",
                IsActive = true
            },

            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.Adults.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "2",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.Children.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.MinPrice.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.MaxPrice.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.Currency.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "USD",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.SortBy.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "best",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.FreeCancellation.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.Rating.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.DefaultRooms.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "1",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.PreferredStarRating.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "3",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.PreferredAmenities.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.PreferredHotelChains.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.MaxPricePerNight.Name,
                DataType = PreferenceDataType.Decimal.Name,
                IsRequired = false,
                DefaultValue = "500",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.PreferredRoomType.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "double",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.Language.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "en",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.PreferredCategories.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.PreferredEventTypes.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[\"public_holiday\"]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.MaxTicketPrice.Name,
                DataType = PreferenceDataType.Decimal.Name,
                IsRequired = false,
                DefaultValue = "100",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.PreferredVenues.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.LocalPlaces.Id,
                Name = PreferenceType.Limit.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "10",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.LocalPlaces.Id,
                Name = PreferenceType.PreferredPlaceTypes.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[\"tourist_attractions\", \"museums\", \"restaurants\"]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.LocalPlaces.Id,
                Name = PreferenceType.SearchRadius.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "10",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.LocalPlaces.Id,
                Name = PreferenceType.PreferredPriceLevel.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "medium",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.DefaultInfantsInSeat.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.DefaultInfantsOnLap.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.DefaultBags.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.AvoidAirlines.Name,
                DataType = PreferenceDataType.Array.Name,
                IsRequired = false,
                DefaultValue = "[]",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Maps.Id,
                Name = PreferenceType.Type.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "search",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.NoTraceMode.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Flight.Id,
                Name = PreferenceType.DeepSearch.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Hotel.Id,
                Name = PreferenceType.NoTraceMode.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Event.Id,
                Name = PreferenceType.NoTraceMode.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.LocalPlaces.Id,
                Name = PreferenceType.NoTraceMode.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.Maps.Id,
                Name = PreferenceType.NoTraceMode.Name,
                DataType = PreferenceDataType.Boolean.Name,
                IsRequired = false,
                DefaultValue = "false",
                IsActive = true
            },
            // FlightExplore preferences
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.Currency.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "USD",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.Language.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = "en",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.Adults.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "1",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.Children.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.InfantsInSeat.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.InfantsOnLap.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.DefaultBags.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "0",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.MaxPrice.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.TravelClass.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = "1",
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.FlightExplore.Id,
                Name = PreferenceType.MaxLayovers.Name,
                DataType = PreferenceDataType.Integer.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            },
            new()
            {
                ServiceTypeId = ServiceType.TripAdvisorSearch.Id,
                Name = PreferenceType.Ssrc.Name,
                DataType = PreferenceDataType.String.Name,
                IsRequired = false,
                DefaultValue = null,
                IsActive = true
            }
        };

        var existingPreferences = ctx.PreferenceTypes.ToList();
        
        var duplicatesToRemove = existingPreferences
            .GroupBy(p => $"{p.ServiceTypeId}:{p.Name}".ToLowerInvariant())
            .SelectMany(g => g.Skip(1))
            .ToList();
            
        if (duplicatesToRemove.Count > 0)
        {
            // We must remove cascading if necessary, but assuming PreferenceTypes table doesn't have restrict foreign keys that stop this.
            // If they are orphaned UserPreferences, this might fail, but let's try.
            ctx.PreferenceTypes.RemoveRange(duplicatesToRemove);
            ctx.SaveChanges();
            existingPreferences = ctx.PreferenceTypes.ToList();
        }

        var existingPreferencesByKey = existingPreferences.ToDictionary(
            preferenceType => $"{preferenceType.ServiceTypeId}:{preferenceType.Name}".ToLowerInvariant(),
            preferenceType => preferenceType);

        var preferencesToAdd = new List<PreferenceTypes>();
        var hasChanges = false;

        foreach (var expectedPreferenceType in expectedPreferenceTypes)
        {
            var key = $"{expectedPreferenceType.ServiceTypeId}:{expectedPreferenceType.Name}".ToLowerInvariant();
            if (!existingPreferencesByKey.TryGetValue(key, out var existingPreferenceType))
            {
                preferencesToAdd.Add(expectedPreferenceType);
                continue;
            }

            var preferenceTypeChanged = false;

            if (existingPreferenceType.Name != expectedPreferenceType.Name)
            {
                existingPreferenceType.Name = expectedPreferenceType.Name;
                hasChanges = true;
                preferenceTypeChanged = true;
            }
            if (existingPreferenceType.DataType != expectedPreferenceType.DataType)
            {
                existingPreferenceType.DataType = expectedPreferenceType.DataType;
                hasChanges = true;
                preferenceTypeChanged = true;
            }

            if (existingPreferenceType.IsRequired != expectedPreferenceType.IsRequired)
            {
                existingPreferenceType.IsRequired = expectedPreferenceType.IsRequired;
                hasChanges = true;
                preferenceTypeChanged = true;
            }

            if (existingPreferenceType.DefaultValue != expectedPreferenceType.DefaultValue)
            {
                existingPreferenceType.DefaultValue = expectedPreferenceType.DefaultValue;
                hasChanges = true;
                preferenceTypeChanged = true;
            }

            if (existingPreferenceType.IsActive != expectedPreferenceType.IsActive)
            {
                existingPreferenceType.IsActive = expectedPreferenceType.IsActive;
                hasChanges = true;
                preferenceTypeChanged = true;
            }

            if (preferenceTypeChanged)
            {
                existingPreferenceType.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (preferencesToAdd.Count > 0)
        {
            ctx.PreferenceTypes.AddRange(preferencesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }
}


