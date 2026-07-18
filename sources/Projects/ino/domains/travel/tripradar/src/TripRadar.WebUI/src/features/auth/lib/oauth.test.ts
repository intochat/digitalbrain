import { beforeEach, describe, expect, it, vi } from 'vitest';

const hoisted = vi.hoisted(() => {
  const mockSignInWithPopup = vi.fn();
  const mockSignInWithRedirect = vi.fn();
  const mockGetRedirectResult = vi.fn();

  class MockGoogleAuthProvider {
    public static credentialFromResult = vi.fn();

    public addScope = vi.fn();
  }

  return {
    mockSignInWithPopup,
    mockSignInWithRedirect,
    mockGetRedirectResult,
    MockGoogleAuthProvider,
  };
});

vi.mock('firebase/auth', () => ({
  GoogleAuthProvider: hoisted.MockGoogleAuthProvider,
  signInWithPopup: hoisted.mockSignInWithPopup,
  signInWithRedirect: hoisted.mockSignInWithRedirect,
  getRedirectResult: hoisted.mockGetRedirectResult,
}));

vi.mock('shared/lib/firebase', () => ({
  auth: {},
}));

vi.mock('shared/api', () => ({
  apiClient: {
    post: vi.fn(),
  },
}));

vi.mock('shared/lib', () => ({
  mapProfileToAuthUser: vi.fn((profile: { username: string; email: string }) => ({
    username: profile.username,
    email: profile.email,
    name: profile.username,
    avatar: 'https://example.com/avatar.png',
    subscription: 'free',
  })),
}));

vi.mock('entities/user/api', () => ({
  profileApi: {
    getProfile: vi.fn(),
  },
}));

vi.mock('shared/store/auth', () => ({
  useAuthStore: {
    getState: () => ({
      login: vi.fn(),
    }),
  },
}));

import { handleGoogleSignUp, processGoogleRedirectSignIn } from './oauth';

describe('oauth helpers', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
  });

  it('should not process redirect result when redirect flow was not started', async () => {
    const result = await processGoogleRedirectSignIn();

    expect(result).toBeNull();
    expect(hoisted.mockGetRedirectResult).not.toHaveBeenCalled();
  });

  it('should fallback to redirect mode when popup is blocked', async () => {
    hoisted.mockSignInWithPopup.mockRejectedValue({
      code: 'auth/popup-blocked',
      message: 'Popup was blocked',
    });
    hoisted.mockSignInWithRedirect.mockResolvedValue(undefined);

    const result = await handleGoogleSignUp();

    expect(result).toEqual({ success: true, redirecting: true });
    expect(hoisted.mockSignInWithRedirect).toHaveBeenCalledTimes(1);
    expect(sessionStorage.getItem('google_oauth_redirect_pending')).toBe('1');
  });
});
