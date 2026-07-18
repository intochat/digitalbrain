import { useCallback, useReducer } from 'react';
import type { UserPreferences } from 'shared/api';

export interface PreferencesFormState {
  preferences: UserPreferences;
  isDirty: boolean;
  isSubmitting: boolean;
  errors: Record<string, string>;
}

export type PreferencesFormAction =
  | { type: 'SET_PREFERENCES'; payload: UserPreferences }
  | { type: 'UPDATE_PREFERENCE'; payload: { service: keyof UserPreferences; field: string; value: unknown } }
  | { type: 'SET_ERROR'; payload: { field: string; error: string } }
  | { type: 'CLEAR_ERROR'; payload: { field: string } }
  | { type: 'SET_SUBMITTING'; payload: boolean }
  | { type: 'RESET_FORM'; payload: UserPreferences };

const preferencesFormReducer = (state: PreferencesFormState, action: PreferencesFormAction): PreferencesFormState => {
  switch (action.type) {
    case 'SET_PREFERENCES':
      return {
        ...state,
        preferences: action.payload,
        isDirty: false,
        errors: {},
      };

    case 'UPDATE_PREFERENCE': {
      const { service, field, value } = action.payload;
      const currentServicePrefs = state.preferences[service] || {};
      const errorKey = `${service}.${field}`;
      const newErrors = { ...state.errors };
      delete newErrors[errorKey];

      return {
        ...state,
        preferences: {
          ...state.preferences,
          [service]: {
            ...currentServicePrefs,
            [field]: value,
          },
        },
        isDirty: true,
        errors: newErrors,
      };
    }

    case 'SET_ERROR':
      return {
        ...state,
        errors: {
          ...state.errors,
          [action.payload.field]: action.payload.error,
        },
      };

    case 'CLEAR_ERROR': {
      const newErrors = { ...state.errors };
      delete newErrors[action.payload.field];
      return {
        ...state,
        errors: newErrors,
      };
    }

    case 'SET_SUBMITTING':
      return {
        ...state,
        isSubmitting: action.payload,
      };

    case 'RESET_FORM':
      return {
        preferences: action.payload,
        isDirty: false,
        isSubmitting: false,
        errors: {},
      };

    default:
      return state;
  }
};

export interface UsePreferencesFormOptions {
  initialPreferences?: UserPreferences;
  onSubmit?: (preferences: UserPreferences) => Promise<void>;
}

export const usePreferencesForm = ({ initialPreferences = {}, onSubmit }: UsePreferencesFormOptions = {}) => {
  const [state, dispatch] = useReducer(preferencesFormReducer, {
    preferences: initialPreferences,
    isDirty: false,
    isSubmitting: false,
    errors: {},
  });

  const updatePreference = useCallback(
    <T extends keyof UserPreferences>(service: T, field: keyof NonNullable<UserPreferences[T]>, value: unknown) => {
      // Validate the value
      const validationError = validatePreferenceField(field as string, value);

      if (validationError) {
        dispatch({
          type: 'SET_ERROR',
          payload: { field: `${service}.${String(field)}`, error: validationError },
        });
        return;
      }

      dispatch({
        type: 'UPDATE_PREFERENCE',
        payload: { service, field: field as string, value },
      });
    },
    []
  );

  const setError = useCallback((field: string, error: string) => {
    dispatch({ type: 'SET_ERROR', payload: { field, error } });
  }, []);

  const clearError = useCallback((field: string) => {
    dispatch({ type: 'CLEAR_ERROR', payload: { field } });
  }, []);

  const resetForm = useCallback((preferences: UserPreferences = {}) => {
    dispatch({ type: 'RESET_FORM', payload: preferences });
  }, []);

  const submitForm = useCallback(async () => {
    if (!onSubmit) return;

    // Validate all fields before submission
    const validationErrors = validateAllPreferences(state.preferences);

    if (Object.keys(validationErrors).length > 0) {
      Object.entries(validationErrors).forEach(([field, error]) => {
        dispatch({ type: 'SET_ERROR', payload: { field, error } });
      });
      return;
    }

    dispatch({ type: 'SET_SUBMITTING', payload: true });

    try {
      await onSubmit(state.preferences);
      dispatch({ type: 'SET_SUBMITTING', payload: false });
    } catch (error) {
      dispatch({ type: 'SET_SUBMITTING', payload: false });
      throw error;
    }
  }, [state.preferences, onSubmit]);

  const setPreferences = useCallback((preferences: UserPreferences) => {
    dispatch({ type: 'SET_PREFERENCES', payload: preferences });
  }, []);

  return {
    ...state,
    updatePreference,
    setError,
    clearError,
    resetForm,
    submitForm,
    setPreferences,
  };
};

// Validation functions
const validatePreferenceField = (field: string, value: unknown): string | null => {
  // Basic number field validations
  if (typeof value === 'number') {
    // Adults must be at least 1 (logical requirement)
    if (field === 'Adults' && value < 1) {
      return 'Adults must be at least 1';
    }

    // Children, infants cannot be negative
    if ((field === 'Children' || field === 'InfantsInSeat' || field === 'InfantsOnLap') && value < 0) {
      return 'Value cannot be negative';
    }

    // Prices cannot be negative
    if ((field === 'MaxPrice' || field === 'MinPrice') && value < 0) {
      return 'Price cannot be negative';
    }

    // Forecast days must be positive
    if (field === 'ForecastDays' && value < 1) {
      return 'Forecast days must be at least 1';
    }

    // Rating must be between 1 and 5 (standard rating scale)
    if (field === 'MinRating' && (value < 1 || value > 5)) {
      return 'Rating must be between 1 and 5';
    }

    // Radius must be positive
    if (field === 'Radius' && value <= 0) {
      return 'Radius must be positive';
    }

    // Limit must be positive
    if (field === 'Limit' && value < 1) {
      return 'Limit must be at least 1';
    }
  }

  // String field validations
  if (typeof value === 'string') {
    // Currency code validation (ISO 4217 format)
    if (field === 'Currency' && value && !/^[A-Z]{3}$/.test(value)) {
      return 'Currency must be a valid 3-letter code (e.g., USD, EUR)';
    }
  }

  return null;
};

const validateAllPreferences = (preferences: UserPreferences): Record<string, string> => {
  const errors: Record<string, string> = {};

  Object.entries(preferences).forEach(([service, servicePrefs]) => {
    if (servicePrefs) {
      Object.entries(servicePrefs).forEach(([field, value]) => {
        if (value !== null && value !== undefined) {
          const error = validatePreferenceField(field, value);
          if (error) {
            errors[`${service}.${field}`] = error;
          }
        }
      });
    }
  });

  return errors;
};
