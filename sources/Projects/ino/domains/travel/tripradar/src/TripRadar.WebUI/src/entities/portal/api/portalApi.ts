import { apiClient } from 'shared/api';
import type { components } from 'shared/api/generated-types';
import type { CurrencyResponse } from 'shared/lib/currency/currencyPresentation';

export type { CurrencyResponse } from 'shared/lib/currency/currencyPresentation';
export type GetLanguagesResponse = components['schemas']['GetLanguagesResponse'];

export interface GetCurrenciesResponse {
  currencies: CurrencyResponse[];
}

export interface TimezoneResponse {
  timezoneId: number;
  timezoneCode: string;
  timezoneName: string;
}

export interface GetTimezonesResponse {
  timezones: TimezoneResponse[];
}

export const portalApi = {
  getLanguages: async (): Promise<GetLanguagesResponse> => {
    return apiClient.get('/api/v1/portal/languages');
  },
  getCurrencies: async (): Promise<GetCurrenciesResponse> => {
    return apiClient.get('/api/v1/portal/currencies');
  },
  getTimezones: async (): Promise<GetTimezonesResponse> => {
    return apiClient.get('/api/v1/portal/timezones');
  },
};
