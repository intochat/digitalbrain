import { create } from 'zustand';
import type { User } from 'app/types';
import type { GetUserProfileResponse } from 'shared/api';
import { mapProfileToAuthUser } from 'shared/lib/auth-user';

type ProfileFetcher = () => Promise<GetUserProfileResponse>;

interface AuthState {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (user: User) => void;
  logout: () => void;
  updateUser: (updates: Partial<User>) => void;
  initializeAuth: (fetchProfile: ProfileFetcher) => Promise<void>;
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  user: null,
  isAuthenticated: false,
  isLoading: true,

  login: user =>
    set({
      user: {
        ...user,
        subscription: user.subscription || 'free',
      },
      isAuthenticated: true,
    }),

  logout: () => {
    set({ user: null, isAuthenticated: false });
  },

  updateUser: updates => {
    const { user } = get();
    if (user) {
      set({ user: { ...user, ...updates } });
    }
  },

  initializeAuth: async (fetchProfile: ProfileFetcher) => {
    set({ isLoading: true });

    try {
      const profile = await fetchProfile();
      set({
        user: mapProfileToAuthUser(profile),
        isAuthenticated: true,
        isLoading: false,
      });
    } catch {
      set({
        user: null,
        isAuthenticated: false,
        isLoading: false,
      });
    }
  },
}));
