import { frontendI18n } from 'app/i18n';

export interface TripVaultFormState {
  name: string;
  description: string;
  startDate: string;
  endDate: string;
}

export interface ApiErrorResponseShape {
  errorCode?: string;
  code?: string;
}

export interface ApiErrorShape {
  code?: string;
  response?: {
    data?: ApiErrorResponseShape;
  };
}

export const TRIPS_PAGE_SIZES = [4, 8, 12] as const;
export const DEFAULT_TRIPS_PAGE_SIZE = TRIPS_PAGE_SIZES[0];
export const ACTIVE_TRIP_VAULT_STORAGE_KEY = 'tripradar.activeTripVaultUniqueId';
export const FIRST_SAVED_TRIP_EVENT_STORAGE_KEY = 'tripradar.telemetry.firstSavedTrip.v1';
export const TRIP_VAULT_NAME_EXISTS_ERROR_CODE = 'TRIP_VAULT_NAME_EXISTS';
export const DUPLICATE_TRIP_NAME_MESSAGE = frontendI18n.t(
  'Trip name must be unique. A vault with this name already exists.'
);

export const createInitialFormState = (): TripVaultFormState => ({
  name: '',
  description: '',
  startDate: '',
  endDate: '',
});

export const toDateInputValue = (value?: string | null): string => {
  if (!value) return '';

  const isoDateMatch = value.match(/^\d{4}-\d{2}-\d{2}/);
  if (isoDateMatch) return isoDateMatch[0];

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';

  const timezoneOffsetInMs = parsed.getTimezoneOffset() * 60 * 1000;
  return new Date(parsed.getTime() - timezoneOffsetInMs).toISOString().slice(0, 10);
};

export const toApiDate = (value: string): string | null => {
  if (!value) return null;
  return `${value}T00:00:00.000Z`;
};

export const getTodayDateInputValue = (): string => {
  const now = new Date();
  const timezoneOffsetInMs = now.getTimezoneOffset() * 60 * 1000;
  return new Date(now.getTime() - timezoneOffsetInMs).toISOString().slice(0, 10);
};

export const formatDateTime = (value?: string | null): string => {
  if (!value) return frontendI18n.t('Not set');
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString();
};

export const formatDate = (value?: string | null): string => {
  if (!value) return frontendI18n.t('Not set');
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleDateString();
};

export const copyToClipboard = async (value: string): Promise<boolean> => {
  if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return true;
  }

  if (typeof document === 'undefined') return false;

  const textAreaElement = document.createElement('textarea');
  textAreaElement.value = value;
  textAreaElement.setAttribute('readonly', '');
  textAreaElement.style.position = 'absolute';
  textAreaElement.style.left = '-9999px';

  document.body.appendChild(textAreaElement);
  textAreaElement.select();
  const successful = typeof document.execCommand === 'function' ? document.execCommand('copy') : false;
  document.body.removeChild(textAreaElement);

  return successful;
};

export const normalizeTripName = (value: string): string => value.trim().toLocaleLowerCase();

export const getApiErrorCode = (error: unknown): string | null => {
  if (!error || typeof error !== 'object') return null;
  const typedError = error as ApiErrorShape;
  return typedError.code ?? typedError.response?.data?.errorCode ?? typedError.response?.data?.code ?? null;
};

export const getFormValidationError = (formState: TripVaultFormState, todayDateInputValue: string): string | null => {
  const trimmedName = formState.name.trim();
  const trimmedDescription = formState.description.trim();

  if (!trimmedName) return frontendI18n.t('Trip name is required.');
  if (trimmedName.length > 255) return frontendI18n.t('Trip name cannot exceed 255 characters.');
  if (trimmedDescription.length > 2000) return frontendI18n.t('Description cannot exceed 2000 characters.');
  if (formState.startDate && formState.startDate < todayDateInputValue) {
    return frontendI18n.t('Trip start date cannot be in the past.');
  }
  if (formState.endDate && formState.endDate < todayDateInputValue) {
    return frontendI18n.t('Trip end date cannot be in the past.');
  }
  if (formState.startDate && formState.endDate && formState.endDate < formState.startDate) {
    return frontendI18n.t('Trip end date must be on or after the start date.');
  }
  return null;
};
