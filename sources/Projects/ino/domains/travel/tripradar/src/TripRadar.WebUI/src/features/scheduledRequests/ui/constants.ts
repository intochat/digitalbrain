import type { ScheduledQueryType } from 'entities/scheduledRequests';

export interface QueryTypeOption {
  value: ScheduledQueryType;
  label: string;
  description: string;
}

export interface ScheduleOption {
  value: string;
  label: string;
}

export interface ScheduledRequestFormState {
  queryType: ScheduledQueryType;
  schedule: string;
  nextExecutionTime: string;
  searchQuery: string;
  location: string;
  radius: string;
  departureAirportCode: string;
  destinationAirportCode: string;
  departureDate: string;
  returnDate: string;
  checkInDate: string;
  checkOutDate: string;
  startDate: string;
  endDate: string;
}

export interface ServiceBadgeConfig {
  label: string;
  className: string;
}

export type AirportField = 'departureAirportCode' | 'destinationAirportCode';
export type AirportInputKey = 'departure' | 'destination';

export const QUERY_TYPE_OPTIONS: QueryTypeOption[] = [
  { value: 'flights', label: 'Flights', description: 'Track flight prices and availability between two airports.' },
  { value: 'hotels', label: 'Hotels', description: 'Monitor hotel options for a destination and date range.' },
  { value: 'events', label: 'Events', description: 'Schedule recurring event searches for a location.' },
  {
    value: 'local-places',
    label: 'Local Places',
    description: 'Watch local place recommendations around an area.',
  },
];
