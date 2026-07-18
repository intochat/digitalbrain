import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from 'shared/api';
import type { components } from 'shared/api/generated-types';
import { portalApi } from './portalApi';

vi.mock('shared/api', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

describe('portalApi', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('requests portal languages from backend endpoint', async () => {
    const response: components['schemas']['GetLanguagesResponse'] = {
      languages: [{ languageCode: 'en', languageName: 'English' }],
    };

    const mockGet = vi.mocked(apiClient.get);
    mockGet.mockResolvedValue(response);

    const result = await portalApi.getLanguages();

    expect(mockGet).toHaveBeenCalledWith('/api/v1/portal/languages');
    expect(result).toEqual(response);
  });

  it('requests portal timezones from backend endpoint', async () => {
    const response = {
      timezones: [{ timezoneId: 2, timezoneCode: 'America/New_York', timezoneName: 'Eastern Time (ET)' }],
    };

    const mockGet = vi.mocked(apiClient.get);
    mockGet.mockResolvedValue(response);

    const result = await portalApi.getTimezones();

    expect(mockGet).toHaveBeenCalledWith('/api/v1/portal/timezones');
    expect(result).toEqual(response);
  });
});
