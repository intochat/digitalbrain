import type { UpdateUserPreferencesRequest, UserPreferences, components } from 'shared/api';
import { apiClient } from 'shared/api';

export type GetUserPreferencesResponse = components['schemas']['GetUserPreferencesResponse'];

export interface PreferenceServiceInfo {
  name: string;
  description: string;
}

export interface GetPreferenceServicesResponse {
  services: PreferenceServiceInfo[];
}

export interface PrivacyModeResponse {
  enabled: boolean;
}

export interface PreferenceTypeInfo {
  serviceTypeName: string;
  name: string;
  dataType: string;
  validationSchema?: string | null;
  isRequired: boolean;
  defaultValue?: string | null;
}

export interface GetPreferenceTypesResponse {
  preferenceTypes: PreferenceTypeInfo[];
}

export interface PreferenceServiceTree {
  serviceType: string;
  preferenceTypes: PreferenceTypeInfo[];
}

export interface PreferenceCategoryTree {
  name: string;
  services: PreferenceServiceTree[];
}

export interface GetPreferenceCategoriesResponse {
  categories: PreferenceCategoryTree[];
}

export const preferencesApi = {
  getPreferenceServices: async (): Promise<GetPreferenceServicesResponse> => {
    return apiClient.get('/api/v1/preferences/services');
  },

  getPreferenceTypes: async (): Promise<GetPreferenceTypesResponse> => {
    return apiClient.get('/api/v1/preferences/types');
  },

  getPreferenceTypesByService: async (serviceType: string): Promise<GetPreferenceTypesResponse> => {
    return apiClient.get(`/api/v1/preferences/services/${encodeURIComponent(serviceType)}/types`);
  },

  getPreferenceCategories: async (): Promise<GetPreferenceCategoriesResponse> => {
    return apiClient.get('/api/v1/preferences/categories');
  },

  getUserPreferences: async (): Promise<GetUserPreferencesResponse> => {
    return apiClient.get('/api/v1/preferences/user');
  },

  updateUserPreferences: async (preferences: UserPreferences): Promise<void> => {
    const requestData: UpdateUserPreferencesRequest = { preferences };
    return apiClient.put('/api/v1/preferences/user', requestData);
  },

  getPrivacyMode: async (): Promise<PrivacyModeResponse> => {
    return apiClient.get('/api/v1/preferences/user/privacy-mode');
  },

  updatePrivacyMode: async (enabled: boolean): Promise<void> => {
    return apiClient.put('/api/v1/preferences/user/privacy-mode', { enabled });
  },
};
