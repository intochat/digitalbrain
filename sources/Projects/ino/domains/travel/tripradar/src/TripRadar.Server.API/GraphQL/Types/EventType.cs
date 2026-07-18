using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.GraphQL.Types;

public class EventType : ObjectType<Event>
{
    protected override void Configure(IObjectTypeDescriptor<Event> descriptor)
    {
        descriptor.Description("Represents a Google event result.");

        descriptor.Field(e => e.Title)
            .Type<StringType>()
            .Description("The title of the event.");

        descriptor.Field(e => e.Date)
            .Type<EventDateType>()
            .Description("Information about the event date.");

        descriptor.Field(e => e.Address)
            .Type<ListType<StringType>>()
            .Description("The address of the event venue.");

        descriptor.Field(e => e.Link)
            .Type<StringType>()
            .Description("A link to the event page.");

        descriptor.Field(e => e.Description)
            .Type<StringType>()
            .Description("A brief description of the event.");

        descriptor.Field(e => e.TicketInfo)
            .Type<ListType<TicketInfoType>>()
            .Description("Information about where to buy tickets.");

        descriptor.Field(e => e.Venue)
            .Type<VenueType>()
            .Description("Information about the event venue.");

        descriptor.Field(e => e.Thumbnail)
            .Type<StringType>()
            .Description("A thumbnail image URL for the event.");

        descriptor.Field(e => e.EventLocationMap)
            .Type<EventLocationMapType>()
            .Description("Information about the event location map.");
    }
}

public class EventDateType : ObjectType<EventDate>
{
    protected override void Configure(IObjectTypeDescriptor<EventDate> descriptor)
    {
        descriptor.Description("Represents the date information for an event.");

        descriptor.Field(d => d.StartDate)
            .Type<StringType>()
            .Description("The start date of the event.");

        descriptor.Field(d => d.When)
            .Type<StringType>()
            .Description("A string describing when the event takes place (e.g., 'Today, 6:30 – 8:30 PM').");
    }
}

public class VenueType : ObjectType<Venue>
{
    protected override void Configure(IObjectTypeDescriptor<Venue> descriptor)
    {
        descriptor.Description("Represents information about an event venue.");

        descriptor.Field(v => v.Name)
            .Type<StringType>()
            .Description("The name of the venue.");

        descriptor.Field(v => v.Rating)
            .Type<FloatType>()
            .Description("The rating of the venue.");

        descriptor.Field(v => v.Reviews)
            .Type<IntType>()
            .Description("The number of reviews for the venue.");

        descriptor.Field(v => v.Link)
            .Type<StringType>()
            .Description("A link to the venue's information.");
    }
}

public class TicketInfoType : ObjectType<TicketInfo>
{
    protected override void Configure(IObjectTypeDescriptor<TicketInfo> descriptor)
    {
        descriptor.Description("Represents information about ticket sources for an event.");

        descriptor.Field(t => t.Source)
            .Type<StringType>()
            .Description("The source or platform for tickets.");

        descriptor.Field(t => t.Link)
            .Type<StringType>()
            .Description("A link to the ticket source.");

        descriptor.Field(t => t.LinkType)
            .Type<StringType>()
            .Description("The type of link (e.g., 'tickets', 'more info').");
    }
}

public class EventLocationMapType : ObjectType<EventLocationMap>
{
    protected override void Configure(IObjectTypeDescriptor<EventLocationMap> descriptor)
    {
        descriptor.Description("Represents information about the event's location map.");

        descriptor.Field(m => m.Image)
            .Type<StringType>()
            .Description("An image URL of the event location map.");

        descriptor.Field(m => m.Link)
            .Type<StringType>()
            .Description("A link to the event location on a map.");

        descriptor.Field(m => m.SerpapiLink)
            .Type<StringType>()
            .Description("The SerpAPI link for the event location map.");
    }
}
