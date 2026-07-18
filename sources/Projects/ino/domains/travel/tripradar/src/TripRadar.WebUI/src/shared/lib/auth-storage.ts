interface AuthTokens {
  authToken?: string | null;
  refreshToken?: string | null;
}

export const authStorage = {
  getToken: (): string | null => {
    return null;
  },

  getRefreshToken: (): string | null => {
    return null;
  },

  setTokens: (_tokens: AuthTokens): void => {
    // Tokens are stored in HttpOnly cookies by the backend.
  },

  clearTokens: (): void => {
    // Tokens are cleared via backend logout + expired HttpOnly cookies.
  },

  hasValidToken: (): boolean => {
    return false;
  },
};
