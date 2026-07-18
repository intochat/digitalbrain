import { apiClient } from 'shared/api';
import type { CreateTripVaultRequest, TripQueryHistoryResponse, TripVaultItem, UpdateTripVaultRequest } from './types';

const TRIPS_BASE_PATH = '/api/v1/trips';

const encodePathSegment = (value: string): string => encodeURIComponent(value);

export const tripVaultApi = {
  getUserTrips: async (): Promise<TripVaultItem[]> => {
    return apiClient.get(TRIPS_BASE_PATH);
  },

  createTripVault: async (request: CreateTripVaultRequest): Promise<TripVaultItem> => {
    return apiClient.post(TRIPS_BASE_PATH, request);
  },

  updateTripVault: async (tripUniqueId: string, request: UpdateTripVaultRequest): Promise<TripVaultItem> => {
    const encodedTripId = encodePathSegment(tripUniqueId);
    return apiClient.put(`${TRIPS_BASE_PATH}/${encodedTripId}`, request);
  },

  deleteTripVault: async (tripUniqueId: string): Promise<void> => {
    const encodedTripId = encodePathSegment(tripUniqueId);
    return apiClient.delete(`${TRIPS_BASE_PATH}/${encodedTripId}`);
  },

  getTripQueryHistory: async (
    tripUniqueId: string,
    pageNumber = 1,
    pageSize = 20
  ): Promise<TripQueryHistoryResponse> => {
    const encodedTripId = encodePathSegment(tripUniqueId);
    return apiClient.get(`${TRIPS_BASE_PATH}/${encodedTripId}/history?pageNumber=${pageNumber}&pageSize=${pageSize}`);
  },

  removeTripItemByUniqueId: async (tripUniqueId: string, itemUniqueId: string): Promise<void> => {
    const encodedTripId = encodePathSegment(tripUniqueId);
    const encodedItemId = encodePathSegment(itemUniqueId);
    return apiClient.delete(`${TRIPS_BASE_PATH}/${encodedTripId}/items/by-unique-id/${encodedItemId}`);
  },
};
