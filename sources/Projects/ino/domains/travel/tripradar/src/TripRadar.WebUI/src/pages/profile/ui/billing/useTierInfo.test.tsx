import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useSubscriptionQuery } from 'entities/payment/api';
import { useProfileQuery } from 'entities/user/api';
import { useTierInfo } from './useTierInfo';

vi.mock('app/providers', () => ({
  useFrontendLanguage: () => ({
    t: (value: string) => value,
  }),
}));

vi.mock('entities/payment/api', () => ({
  useSubscriptionQuery: vi.fn(),
}));

vi.mock('entities/user/api', () => ({
  useProfileQuery: vi.fn(),
}));

const mockedUseProfileQuery = vi.mocked(useProfileQuery);
const mockedUseSubscriptionQuery = vi.mocked(useSubscriptionQuery);

describe('useTierInfo', () => {
  it('disables auth-only queries when enabled is false', () => {
    mockedUseProfileQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as never);
    mockedUseSubscriptionQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as never);

    const { result } = renderHook(() => useTierInfo({ enabled: false }));

    expect(mockedUseProfileQuery).toHaveBeenCalledWith({ enabled: false });
    expect(mockedUseSubscriptionQuery).toHaveBeenCalledWith({ enabled: false });
    expect(result.current.tierName).toBe('basic');
    expect(result.current.isBasicTier).toBe(true);
  });

  it('treats missing subscription as basic tier instead of an error', () => {
    mockedUseProfileQuery.mockReturnValue({
      data: { tierName: 'basic' },
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    } as never);
    mockedUseSubscriptionQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: {
        code: 'SUBSCRIPTION_NOT_FOUND',
        response: { status: 404 },
      },
      refetch: vi.fn(),
    } as never);

    const { result } = renderHook(() => useTierInfo());

    expect(result.current.tierName).toBe('basic');
    expect(result.current.isBasicTier).toBe(true);
    expect(result.current.error).toBeNull();
  });
});
