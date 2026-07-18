import { CalendarDays, MapPin, Ticket } from 'lucide-react';
import type { TripHistoryItem } from 'entities/tripVault';
import { GenericCard } from './GenericCard';
import {
  getArray,
  getNumber,
  getObject,
  getString,
  isTruncatedWrapperPayload,
  safeParse,
  truncateText,
} from './parseHistoryData';
import { ResultImage } from './ResultImage';

interface EventCardProps {
  item: TripHistoryItem;
}

interface ParsedEvent {
  title: string;
  date: string | null;
  address: string[];
  link: string | null;
  thumbnail: string | null;
  description: string | null;
  ticketInfo: Array<{ source: string | null; link: string | null }>;
  venue: { name: string | null; rating: number | null; reviews: number | null };
}

const parseEvents = (data: Record<string, unknown>): ParsedEvent[] => {
  const eventsResults =
    getArray(data, 'eventsResults') ?? getArray(data, 'events_results') ?? getArray(data, 'events') ?? [];

  return eventsResults.slice(0, 4).map(event => {
    const e = event as Record<string, unknown>;
    const date = getObject(e, 'date');
    const venueData = getObject(e, 'venue');
    const ticketInfoArray = getArray(e, 'ticketInfo') ?? getArray(e, 'ticket_info') ?? [];

    return {
      title: getString(e, 'title') ?? 'Unknown event',
      date: date
        ? (getString(date, 'startDate') ?? getString(date, 'start_date') ?? getString(date, 'when') ?? null)
        : (getString(e, 'date') ?? null),
      address: (getArray(e, 'address') as string[]) ?? [],
      link: getString(e, 'link'),
      thumbnail: getString(e, 'thumbnail') ?? getString(e, 'image'),
      description: getString(e, 'description'),
      ticketInfo: ticketInfoArray.map(t => {
        const ticket = t as Record<string, unknown>;
        return {
          source: getString(ticket, 'source'),
          link: getString(ticket, 'link'),
        };
      }),
      venue: {
        name: venueData ? getString(venueData, 'name') : null,
        rating: venueData ? getNumber(venueData, 'rating') : null,
        reviews: venueData ? getNumber(venueData, 'reviews') : null,
      },
    };
  });
};

export const EventCard = ({ item }: EventCardProps) => {
  const data = safeParse(item.resultSummary);

  if (!data || isTruncatedWrapperPayload(data)) {
    return <GenericCard item={item} />;
  }

  const events = parseEvents(data);

  return (
    <div className="space-y-3">
      {events.length === 0 && (
        <p className="text-xs text-content-secondary dark:text-content-secondary-dark">
          No events available in the response snapshot.
        </p>
      )}

      {events.map((event, index) => (
        <div
          key={`event-${index}`}
          className="rounded-lg border border-outline/60 dark:border-outline-dark/60 bg-surface dark:bg-surface-dark p-3 space-y-2"
        >
          <div className="flex items-start gap-3">
            <ResultImage src={event.thumbnail} alt={event.title} variant="event" className="h-14 w-14" />
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <p className="text-xs font-semibold text-content dark:text-content-dark">{event.title}</p>
              </div>
              <div className="flex items-center gap-2 mt-1 text-[11px] text-content-secondary dark:text-content-secondary-dark flex-wrap">
                {event.date && (
                  <span className="inline-flex items-center gap-1">
                    <CalendarDays className="h-3 w-3" />
                    {event.date}
                  </span>
                )}
                {event.venue.name && (
                  <span className="inline-flex items-center gap-1">
                    <MapPin className="h-3 w-3" />
                    {event.venue.name}
                  </span>
                )}
              </div>
              {event.address.length > 0 && (
                <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark mt-0.5">
                  {event.address.join(', ')}
                </p>
              )}
            </div>
          </div>

          {event.description && (
            <p className="text-[11px] text-content-secondary dark:text-content-secondary-dark leading-relaxed">
              {truncateText(event.description, 250)}
            </p>
          )}

          {event.ticketInfo.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {event.ticketInfo.map((ticket, tIndex) => (
                <span
                  key={`ticket-${tIndex}`}
                  className="inline-flex items-center gap-1 rounded-full bg-indigo-50 dark:bg-indigo-500/10 px-2.5 py-0.5 text-[11px] font-medium text-indigo-700 dark:text-indigo-300"
                >
                  <Ticket className="h-3 w-3" />
                  {ticket.source ?? 'Tickets'}
                </span>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
};
