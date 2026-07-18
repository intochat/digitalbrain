import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

interface FormState {
  [key: string]: unknown;
}

interface NavigationPersistenceState {
  formData: Record<string, FormState>;
  hasUnsavedChanges: boolean;
  pendingNavigation: string | null;
}

interface UseNavigationPersistenceOptions {
  pageKey: string;
  enableWarning?: boolean;
  onBeforeNavigate?: (targetPath: string) => boolean | Promise<boolean>;
}

interface UseNavigationPersistenceReturn {
  // Form data management
  formData: FormState;
  setFormData: (data: FormState | ((prev: FormState) => FormState)) => void;
  clearFormData: () => void;

  // Unsaved changes tracking
  hasUnsavedChanges: boolean;
  setHasUnsavedChanges: (hasChanges: boolean) => void;

  // Navigation control
  safeNavigate: (path: string) => void;
  confirmNavigation: () => void;
  cancelNavigation: () => void;

  // State
  pendingNavigation: string | null;
}

const STORAGE_KEY = 'profile_navigation_persistence';

/**
 * Hook for managing form data persistence and navigation warnings across profile pages
 */
export const useNavigationPersistence = ({
  pageKey,
  enableWarning = true,
  onBeforeNavigate,
}: UseNavigationPersistenceOptions): UseNavigationPersistenceReturn => {
  const location = useLocation();
  const navigate = useNavigate();
  const [state, setState] = useState<NavigationPersistenceState>(() => {
    // Load persisted state from sessionStorage
    try {
      const stored = sessionStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        return {
          formData: parsed.formData || {},
          hasUnsavedChanges: false, // Reset unsaved changes on page load
          pendingNavigation: null,
        };
      }
    } catch (error) {
      console.warn('Failed to load navigation persistence state:', error);
    }

    return {
      formData: {},
      hasUnsavedChanges: false,
      pendingNavigation: null,
    };
  });

  const stateRef = useRef(state);
  stateRef.current = state;

  // Persist state to sessionStorage whenever it changes
  useEffect(() => {
    try {
      sessionStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({
          formData: state.formData,
        })
      );
    } catch (error) {
      console.warn('Failed to persist navigation state:', error);
    }
  }, [state.formData]);

  // Get form data for current page
  const formData = state.formData[pageKey] || {};

  // Set form data for current page
  const setFormData = useCallback(
    (data: FormState | ((prev: FormState) => FormState)) => {
      setState(prev => ({
        ...prev,
        formData: {
          ...prev.formData,
          [pageKey]: typeof data === 'function' ? data(prev.formData[pageKey] || {}) : data,
        },
      }));
    },
    [pageKey]
  );

  // Clear form data for current page
  const clearFormData = useCallback(() => {
    setState(prev => ({
      ...prev,
      formData: {
        ...prev.formData,
        [pageKey]: {},
      },
    }));
  }, [pageKey]);

  // Set unsaved changes flag
  const setHasUnsavedChanges = useCallback((hasChanges: boolean) => {
    setState(prev => ({
      ...prev,
      hasUnsavedChanges: hasChanges,
    }));
  }, []);

  // Safe navigation with unsaved changes check
  const safeNavigate = useCallback(
    async (path: string) => {
      // Don't warn if navigating to the same page
      if (path === location.pathname) {
        return;
      }

      // Check if there are unsaved changes and warning is enabled
      if (enableWarning && stateRef.current.hasUnsavedChanges) {
        // Call custom before navigate handler if provided
        if (onBeforeNavigate) {
          const canNavigate = await onBeforeNavigate(path);
          if (!canNavigate) {
            return;
          }
        }

        // Set pending navigation to show confirmation dialog
        setState(prev => ({
          ...prev,
          pendingNavigation: path,
        }));
        return;
      }

      // Navigate immediately if no unsaved changes
      navigate(path);
    },
    [location.pathname, enableWarning, onBeforeNavigate, navigate]
  );

  // Confirm navigation (proceed with pending navigation)
  const confirmNavigation = useCallback(() => {
    const { pendingNavigation } = stateRef.current;
    if (pendingNavigation) {
      setState(prev => ({
        ...prev,
        hasUnsavedChanges: false,
        pendingNavigation: null,
      }));
      navigate(pendingNavigation);
    }
  }, [navigate]);

  // Cancel navigation (clear pending navigation)
  const cancelNavigation = useCallback(() => {
    setState(prev => ({
      ...prev,
      pendingNavigation: null,
    }));
  }, []);

  // Handle browser back/forward navigation
  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      if (stateRef.current.hasUnsavedChanges) {
        event.preventDefault();
        event.returnValue = '';
      }
    };

    if (enableWarning) {
      window.addEventListener('beforeunload', handleBeforeUnload);
      return () => {
        window.removeEventListener('beforeunload', handleBeforeUnload);
      };
    }
  }, [enableWarning]);

  // Clear unsaved changes when location changes (successful navigation)
  useEffect(() => {
    setState(prev => ({
      ...prev,
      hasUnsavedChanges: false,
      pendingNavigation: null,
    }));
  }, [location.pathname]);

  return {
    formData,
    setFormData,
    clearFormData,
    hasUnsavedChanges: state.hasUnsavedChanges,
    setHasUnsavedChanges,
    safeNavigate,
    confirmNavigation,
    cancelNavigation,
    pendingNavigation: state.pendingNavigation,
  };
};
