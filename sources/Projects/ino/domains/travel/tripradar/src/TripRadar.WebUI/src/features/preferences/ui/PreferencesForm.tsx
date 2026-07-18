import React, { useCallback, useState, useRef, useId, useMemo } from 'react';
import { Save, AlertCircle, CrownIcon } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers';
import { useToast } from 'app/providers/ToastProvider';
import { useSubscriptionQuery } from 'entities/payment/api';
import { useUpdatePreferencesMutation } from 'entities/preferences/api';
import type { UserPreferences } from 'shared/api';
import { Button } from 'shared/ui';
import { getErrorMessage, isNetworkError, isServerError } from '../../../shared/lib/retry/retryUtils';
import { getPreferenceCategoriesByKeys } from '../lib/categoryConfig';
import { PREFERENCE_NOTIFICATIONS, getValidationErrorNotification } from '../lib/notificationUtils';
import { usePreferencesForm } from '../lib/usePreferencesForm';
import { CategorySection } from './CategorySection';
import { CollapsibleGroup } from './CollapsibleGroup';
import { ErrorStateDisplay } from './ErrorStateDisplay';
import { FieldSection } from './FieldSection';
import { ToggleField } from './ToggleField';

export interface PreferencesFormProps {
  initialPreferences?: UserPreferences;
  enabledPreferenceKeys?: Array<keyof UserPreferences>;
  onSave?: (preferences: UserPreferences) => void;
}

interface CollapsibleState {
  [groupId: string]: boolean;
}

export const PreferencesForm = ({ initialPreferences = {}, enabledPreferenceKeys, onSave }: PreferencesFormProps) => {
  const { t } = useFrontendLanguage();
  const { showSuccess, showError } = useToast();
  const subscriptionQuery = useSubscriptionQuery();
  const updatePreferencesMutation = useUpdatePreferencesMutation();
  const [saveError, setSaveError] = useState<unknown>(null);
  const [retryCount, setRetryCount] = useState(0);
  const formRef = useRef<HTMLDivElement>(null);
  const formId = useId();

  const [expandedGroups, setExpandedGroups] = useState<CollapsibleState>({});
  const categories = useMemo(() => getPreferenceCategoriesByKeys(enabledPreferenceKeys), [enabledPreferenceKeys]);

  const handleSubmit = useCallback(
    async (preferences: UserPreferences) => {
      setSaveError(null);

      try {
        await updatePreferencesMutation.mutateAsync({
          preferences,
        });

        const successNotification = PREFERENCE_NOTIFICATIONS.SAVE_SUCCESS;
        showSuccess(
          t(successNotification.title),
          successNotification.message ? t(successNotification.message) : undefined
        );

        setRetryCount(0);
        onSave?.(preferences);
      } catch (error) {
        console.error('Failed to save preferences:', error);
        setSaveError(error);
        setRetryCount(prev => prev + 1);

        let notification;

        if (isNetworkError(error)) {
          notification = PREFERENCE_NOTIFICATIONS.NETWORK_ERROR;
        } else if (isServerError(error)) {
          notification = PREFERENCE_NOTIFICATIONS.SERVER_ERROR;
        } else {
          notification = {
            ...PREFERENCE_NOTIFICATIONS.SAVE_ERROR,
            message: t('Unable to save preferences: {errorMessage}', { errorMessage: getErrorMessage(error) }),
          };
        }

        showError(t(notification.title), notification.message ? t(notification.message) : undefined);

        throw error;
      }
    },
    [updatePreferencesMutation, showSuccess, showError, onSave]
  );

  const { preferences, isDirty, isSubmitting, errors, updatePreference, submitForm, setPreferences } =
    usePreferencesForm({
      initialPreferences,
      onSubmit: handleSubmit,
    });
  const tierType = subscriptionQuery.data?.tierType?.toLowerCase();
  const isPaidTier =
    !subscriptionQuery.isLoading &&
    !subscriptionQuery.isError &&
    Boolean(subscriptionQuery.data) &&
    (tierType === 'essential' || tierType === 'advanced');
  const deepSearchEnabled = isPaidTier ? (preferences.Flight?.DeepSearch ?? false) : false;

  const activeErrors = useMemo(
    () => Object.entries(errors).filter(([, error]) => typeof error === 'string' && error.trim().length > 0),
    [errors]
  );

  React.useEffect(() => {
    const errorCount = activeErrors.length;
    if (errorCount > 0 && isDirty) {
      const notification = getValidationErrorNotification(errorCount);
      showError(t(notification.title), notification.message ? t(notification.message) : undefined);
    }
  }, [activeErrors, isDirty, showError, t]);

  React.useEffect(() => {
    setPreferences(initialPreferences);
  }, [initialPreferences, setPreferences]);

  const handleSave = useCallback(async () => {
    try {
      await submitForm();
    } catch {
      // Error handling is done in handleSubmit
    }
  }, [submitForm]);

  const handleRetry = useCallback(async () => {
    try {
      await submitForm();
    } catch {
      // Error handling is done in handleSubmit
    }
  }, [submitForm]);

  const handleDismissError = useCallback(() => {
    setSaveError(null);
    setRetryCount(0);
  }, []);

  const [isAutoSaving, setIsAutoSaving] = useState(false);

  const handleGlobalDeepSearchChange = useCallback(
    async (value: boolean) => {
      updatePreference('Flight', 'DeepSearch', value);

      const updatedPreferences = {
        ...preferences,
        Flight: {
          ...(preferences.Flight || {}),
          DeepSearch: value,
        },
      };

      setIsAutoSaving(true);
      try {
        await handleSubmit(updatedPreferences);
      } catch {
        // Error handling is inside handleSubmit
      } finally {
        setIsAutoSaving(false);
      }
    },
    [updatePreference, preferences, handleSubmit]
  );

  const toggleGroup = useCallback((groupId: string) => {
    setExpandedGroups(prev => ({
      ...prev,
      [groupId]: !prev[groupId],
    }));
  }, []);

  const handleFormKeyDown = useCallback((event: React.KeyboardEvent) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      const groupHeaders = formRef.current?.querySelectorAll('[role="button"][aria-expanded]');
      if (!groupHeaders) return;

      const currentIndex = Array.from(groupHeaders).findIndex(header => header === event.target);
      if (currentIndex === -1) return;

      event.preventDefault();

      let nextIndex;
      if (event.key === 'ArrowDown') {
        nextIndex = currentIndex + 1 >= groupHeaders.length ? 0 : currentIndex + 1;
      } else {
        nextIndex = currentIndex - 1 < 0 ? groupHeaders.length - 1 : currentIndex - 1;
      }

      (groupHeaders[nextIndex] as HTMLElement).focus();
    }
  }, []);

  return (
    <div
      ref={formRef}
      id={formId}
      className="space-y-6"
      onKeyDown={handleFormKeyDown}
      role="form"
      aria-label={t('User preferences form')}
    >
      {saveError ? (
        <ErrorStateDisplay
          error={saveError}
          onRetry={handleRetry}
          onDismiss={handleDismissError}
          isRetrying={isSubmitting}
          retryCount={retryCount}
          maxRetries={3}
        />
      ) : null}

      {activeErrors.length > 0 && (
        <div className="border border-red-200 dark:border-red-800/50 rounded-lg p-3">
          <div className="flex items-start gap-2">
            <AlertCircle className="h-4 w-4 text-red-500 dark:text-red-400 flex-shrink-0 mt-0.5" />
            <div className="flex-1 min-w-0">
              <p className="text-xs font-medium text-content dark:text-content-dark mb-1">
                {t('Please fix the following errors:')}
              </p>
              <ul className="text-xs text-content-secondary dark:text-content-secondary-dark space-y-0.5">
                {activeErrors.map(([field, error]) => (
                  <li key={field}>
                    <span className="text-content-muted">·</span> {field.replace(/\./g, ' → ')}: {error}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      )}

      <CategorySection title="Global Preferences">
        <FieldSection>
          <ToggleField
            className="sm:col-span-2 lg:col-span-3"
            label="Deep search"
            description="More accurate answers, but search takes longer."
            value={deepSearchEnabled}
            onChange={handleGlobalDeepSearchChange}
            disabled={isSubmitting || isAutoSaving || !isPaidTier}
          />
          {!isPaidTier && (
            <div className="sm:col-span-2 lg:col-span-3 flex items-center gap-1.5">
              <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-400">
                <CrownIcon className="h-2.5 w-2.5" />
                {t('Paid only')}
              </span>
              <span className="text-xs text-content-muted dark:text-content-muted-dark">
                {t('Deep search is available only for paid plans')}
              </span>
            </div>
          )}
        </FieldSection>
      </CategorySection>

      {categories.map(category => (
        <CategorySection key={category.id} title={category.title} description={category.description}>
          {category.groups.map(group => {
            const Component = group.component;
            const groupId = `${category.id}-${group.id}`;
            const isExpanded = expandedGroups[groupId] || false;

            return (
              <CollapsibleGroup
                key={group.id}
                title={group.title}
                isExpanded={isExpanded}
                onToggle={() => toggleGroup(groupId)}
              >
                <Component
                  preferences={preferences[group.key] || {}}
                  onChange={(field: string, value: unknown) =>
                    updatePreference(group.key, field as keyof NonNullable<UserPreferences[typeof group.key]>, value)
                  }
                  errors={Object.fromEntries(
                    Object.entries(errors)
                      .filter(([errorKey]) => errorKey.startsWith(`${group.key}.`))
                      .map(([errorKey, errorValue]) => [errorKey.replace(`${group.key}.`, ''), errorValue])
                  )}
                  disabled={isSubmitting}
                />
              </CollapsibleGroup>
            );
          })}
        </CategorySection>
      ))}

      <div className="flex justify-end pt-4 border-t border-outline dark:border-outline-dark">
        <Button
          variant={isDirty ? 'primary' : 'secondary'}
          size="sm"
          onClick={handleSave}
          disabled={!isDirty || isSubmitting}
          isLoading={isSubmitting}
          className="gap-1.5"
        >
          <Save className="w-3.5 h-3.5" />
          {t('Save Preferences')}
        </Button>
      </div>
    </div>
  );
};
