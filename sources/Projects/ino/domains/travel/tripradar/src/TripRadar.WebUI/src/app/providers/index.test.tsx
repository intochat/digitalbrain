import { render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Providers } from './index';

const { trackPageViewMock } = vi.hoisted(() => ({
  trackPageViewMock: vi.fn(),
}));

vi.mock('shared/lib', async () => {
  const actual = await vi.importActual<typeof import('shared/lib')>('shared/lib');
  return {
    ...actual,
    trackPageView: trackPageViewMock,
  };
});

vi.mock('shared/store/auth', () => ({
  useAuthStore: (
    selector?: (state: { initializeAuth: () => void; isLoading: boolean; isAuthenticated: boolean }) => unknown
  ) => {
    const state = {
      initializeAuth: vi.fn(),
      isLoading: false,
      isAuthenticated: false,
    };

    return selector ? selector(state) : state;
  },
}));

vi.mock('entities/user/api', () => ({
  useProfileQuery: () => ({
    data: null,
    isLoading: false,
    isFetching: false,
    isError: false,
  }),
}));

describe('Providers route metadata', () => {
  afterEach(() => {
    document.title = '';
    document.head.querySelector('link[rel="canonical"]')?.remove();
    for (const selector of [
      'meta[name="description"]',
      'meta[property="og:title"]',
      'meta[property="og:description"]',
      'meta[property="og:type"]',
      'meta[property="og:url"]',
      'meta[property="og:image"]',
      'meta[name="twitter:card"]',
      'meta[name="twitter:title"]',
      'meta[name="twitter:description"]',
      'meta[name="twitter:image"]',
    ]) {
      document.head.querySelector(selector)?.remove();
    }
    trackPageViewMock.mockClear();
    window.history.pushState({}, '', '/');
  });

  it('applies changelog metadata for /changelog', async () => {
    window.history.pushState({}, '', '/changelog');

    render(
      <Providers>
        <div>content</div>
      </Providers>
    );

    await waitFor(() => {
      expect(document.title).toBe('TripRadar Changelog - Product Updates and Release Notes');
    });

    expect(document.querySelector('meta[name="description"]')).toHaveAttribute(
      'content',
      'Read the latest TripRadar product updates, release notes, improvements, and fixes in one public timeline.'
    );
    expect(document.querySelector('link[rel="canonical"]')).toHaveAttribute('href', 'https://tripradar.io/changelog');
    expect(trackPageViewMock).toHaveBeenCalledWith('/changelog');
  });
});
