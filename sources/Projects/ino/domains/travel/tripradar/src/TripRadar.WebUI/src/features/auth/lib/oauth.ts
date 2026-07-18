import {
  GoogleAuthProvider,
  getRedirectResult,
  signInWithPopup,
  signInWithRedirect,
  type UserCredential,
} from 'firebase/auth';
import { profileApi } from 'entities/user/api';
import { apiClient, type CreateGoogleLoginRequest, type GetLoginResponse } from 'shared/api';
import { mapProfileToAuthUser } from 'shared/lib';
import { auth } from 'shared/lib/firebase';
import { useAuthStore } from 'shared/store/auth';

type OAuthProviderType = 'google';
const googleRedirectSessionKey = 'google_oauth_redirect_pending';

export interface OAuthResult {
  success: boolean;
  redirecting?: boolean;
  error?: string;
  telegramRequiredEmail?: string;
}

interface OAuthApiErrorData {
  code?: string;
  errorCode?: string;
  type?: string;
  error?: string;
  message?: string;
  detail?: string;
  email?: string;
  reason?: string;
  errorReason?: string;
  Code?: string;
  ErrorCode?: string;
  Type?: string;
  Error?: string;
  Message?: string;
  Detail?: string;
  Email?: string;
  Reason?: string;
  ErrorReason?: string;
  [key: string]: unknown;
}

interface OAuthApiError {
  code?: string;
  message?: string;
  email?: string;
  isTelegramRequired?: boolean;
  response?: {
    status?: number;
    data?: OAuthApiErrorData;
  };
}

const missingFirebaseConfigurationMessage = 'Google sign-in is not configured for this environment.';
const isRedirectFallbackError = (code?: string): boolean => {
  return (
    code === 'auth/popup-blocked' ||
    code === 'auth/cancelled-popup-request' ||
    code === 'auth/operation-not-supported-in-this-environment'
  );
};

const getCurrentHostname = (): string => {
  return typeof window !== 'undefined' ? window.location.hostname : 'this host';
};

const getApiErrorCode = (error: OAuthApiError): string | undefined => {
  const data = error.response?.data;
  return (
    data?.errorCode ||
    data?.ErrorCode ||
    data?.code ||
    data?.Code ||
    data?.type ||
    data?.Type ||
    data?.error ||
    data?.Error ||
    error.code
  );
};

const getApiErrorEmail = (error: OAuthApiError, fallbackEmail?: string): string | undefined => {
  const data = error.response?.data;
  return (
    error.email ||
    data?.email ||
    data?.Email ||
    data?.reason ||
    data?.Reason ||
    data?.errorReason ||
    data?.ErrorReason ||
    fallbackEmail
  );
};

const isTelegramRequiredError = (error: OAuthApiError): boolean => {
  const status = error.response?.status;
  const errorCode = getApiErrorCode(error);
  return (
    error.isTelegramRequired === true ||
    errorCode === 'TELEGRAM_REQUIRED' ||
    ((status === 400 || status === 403) && errorCode === 'TELEGRAM_REQUIRED')
  );
};

const getBackendOAuthErrorMessage = (error: OAuthApiError): string => {
  const errorCode = getApiErrorCode(error);
  const data = error.response?.data;

  switch (errorCode) {
    case 'EMAIL_NOT_CONFIRMED':
      return 'Please confirm your email before signing in.';
    case 'USER_IP_NOT_VALID_OR_NOT_PROVIDED':
      return 'Unable to verify your network address. Please refresh the page and try again.';
    case 'INVALID_TOKEN':
      return 'Google sign-in token is invalid. Please try again.';
    default: {
      const serverMessage = data?.detail || data?.Detail || data?.message || data?.Message || error.message;

      if (serverMessage && serverMessage !== 'API request failed') {
        return serverMessage;
      }

      return 'Unable to complete Google sign-in. Please try again.';
    }
  }
};

const handleOAuthError = (error: { code?: string; message?: string }, provider: string): string => {
  console.error(`${provider} OAuth error:`, error);

  switch (error.code) {
    case 'auth/popup-closed-by-user':
      return '';
    case 'auth/cancelled-popup-request':
      return 'Authorization request was cancelled';
    case 'auth/account-exists-with-different-credential':
      return 'Account already exists with different sign-in method';
    case 'auth/popup-blocked':
      return 'Popup window was blocked by browser';
    case 'auth/unauthorized-domain':
      return `Domain "${getCurrentHostname()}" is not authorized for Google sign-in. Add it to Firebase authorized domains and Google OAuth JavaScript origins.`;
    case 'auth/operation-not-supported-in-this-environment':
      return 'Popup login is not supported in this environment. Please try again (redirect mode will be used automatically).';
    case 'auth/network-request-failed':
      return 'Network error during Google sign-in. Check browser access to Firebase/Auth domains.';
    default:
      return 'Google sign-in failed. Please try again.';
  }
};

const finalizeGoogleLogin = async (result: UserCredential): Promise<OAuthResult> => {
  const credential = GoogleAuthProvider.credentialFromResult(result);
  const googleIdToken = credential?.idToken;

  if (!googleIdToken) {
    return { success: false, error: 'Failed to get Google ID token from OAuth response' };
  }

  let loginResponse: GetLoginResponse;
  try {
    loginResponse = await apiClient.post<GetLoginResponse, CreateGoogleLoginRequest>('/api/v1/tokens/sessions/google', {
      id_token: googleIdToken,
    });
  } catch (error: unknown) {
    const authError = error as OAuthApiError;
    if (isTelegramRequiredError(authError)) {
      const telegramRequiredEmail = getApiErrorEmail(authError, result.user.email ?? undefined);
      if (telegramRequiredEmail) {
        return { success: false, telegramRequiredEmail };
      }
      return { success: false, error: 'Telegram username is required to complete sign-in.' };
    }

    return { success: false, error: getBackendOAuthErrorMessage(authError) };
  }

  try {
    const profile = await profileApi.getProfile({ skipUnauthorizedRedirect: true });
    useAuthStore.getState().login(mapProfileToAuthUser(profile));
  } catch {
    return { success: false, error: 'Google sign-in succeeded, but profile loading failed. Please try again.' };
  }

  sessionStorage.removeItem(googleRedirectSessionKey);

  // Defer navigation when a Telegram bind is pending — the caller (TelegramGoogleAuth page)
  // completes the bind and renders its own success screen.
  const hasPendingTelegramBind = (() => {
    try {
      return Boolean(window.sessionStorage.getItem('tripradar.telegramBind.chatId'));
    } catch {
      return false;
    }
  })();

  if (!hasPendingTelegramBind) {
    window.location.assign('/profile');
  }
  return { success: true };
};

const signInWithProvider = async (
  provider: GoogleAuthProvider,
  providerName: OAuthProviderType
): Promise<OAuthResult> => {
  if (!auth) {
    return { success: false, error: missingFirebaseConfigurationMessage };
  }

  try {
    const popupResult = await signInWithPopup(auth, provider);
    return await finalizeGoogleLogin(popupResult);
  } catch (error: unknown) {
    const authError = error as OAuthApiError;

    if (isTelegramRequiredError(authError)) {
      const telegramRequiredEmail = getApiErrorEmail(authError);
      if (telegramRequiredEmail) {
        return { success: false, telegramRequiredEmail };
      }
      return { success: false, error: 'Telegram username is required to complete sign-in.' };
    }

    if (isRedirectFallbackError(authError.code)) {
      sessionStorage.setItem(googleRedirectSessionKey, '1');
      await signInWithRedirect(auth, provider);
      return { success: true, redirecting: true };
    }

    const errorMessage = handleOAuthError(authError, providerName);
    return { success: false, error: errorMessage };
  }
};

const createGoogleProvider = (): GoogleAuthProvider => {
  const provider = new GoogleAuthProvider();
  provider.addScope('email');
  provider.addScope('profile');
  return provider;
};

export const processGoogleRedirectSignIn = async (): Promise<OAuthResult | null> => {
  if (sessionStorage.getItem(googleRedirectSessionKey) !== '1') {
    return null;
  }

  if (!auth) {
    sessionStorage.removeItem(googleRedirectSessionKey);
    return { success: false, error: missingFirebaseConfigurationMessage };
  }

  try {
    const result = await getRedirectResult(auth);
    if (!result) {
      sessionStorage.removeItem(googleRedirectSessionKey);
      return { success: false, error: 'Google sign-in was interrupted. Please try again.' };
    }

    return await finalizeGoogleLogin(result);
  } catch (error: unknown) {
    sessionStorage.removeItem(googleRedirectSessionKey);
    const authError = error as OAuthApiError;

    if (isTelegramRequiredError(authError)) {
      const telegramRequiredEmail = getApiErrorEmail(authError);
      if (telegramRequiredEmail) {
        return { success: false, telegramRequiredEmail };
      }
      return { success: false, error: 'Telegram username is required to complete sign-in.' };
    }

    const errorMessage = handleOAuthError(authError, 'google');
    return { success: false, error: errorMessage };
  }
};

export const handleGoogleSignUp = async (): Promise<OAuthResult> => {
  if (!auth) {
    return { success: false, error: missingFirebaseConfigurationMessage };
  }

  const provider = createGoogleProvider();
  return signInWithProvider(provider, 'google');
};
