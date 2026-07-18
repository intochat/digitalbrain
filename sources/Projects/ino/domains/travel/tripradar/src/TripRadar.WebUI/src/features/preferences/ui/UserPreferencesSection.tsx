import { useMemo } from 'react';
import { AlertCircle } from 'lucide-react';
import { useFrontendLanguage } from 'app/providers/FrontendLanguageContext';
import { usePreferenceServicesQuery, usePreferenceTypesQuery, useUserPreferencesQuery } from 'entities/preferences/api';
import type { User } from 'shared/api/types';
import { useAuthStore } from 'shared/store/auth';
import { SectionError } from 'shared/ui';
import { mapServiceTypeNamesToPreferenceKeys } from '../lib/categoryConfig';
import { PreferencesErrorBoundary } from './PreferencesErrorBoundary';
import { PreferencesForm } from './PreferencesForm';
import { PreferencesSkeleton } from './PreferencesSkeleton';

export interface UserPreferencesSectionProps {
  className?: string;
}

export const UserPreferencesSection = ({ className = '' }: UserPreferencesSectionProps) => {
  const { t } = useFrontendLanguage();
  const { user } = useAuthStore();

  if (!user) {
    return (
      <div className={`flex items-center gap-2 text-content-secondary dark:text-content-secondary-dark ${className}`}>
        <AlertCircle className="w-4 h-4 flex-shrink-0" />
        <p className="text-sm">{t('preferences.travel.loginRequired')}</p>
      </div>
    );
  }

  return <UserPreferencesContent user={user} className={className} />;
};

interface UserPreferencesContentProps {
  user: User;
  className?: string;
}

const parsePreferenceValue = (rawValue: string | null | undefined, mode: 'object' | 'primitive'): unknown => {
  const trimmedValue = rawValue?.trim();
  if (!trimmedValue) {
    return mode === 'object' ? {} : '';
  }

  try {
    return JSON.parse(trimmedValue);
  } catch {
    return mode === 'object' ? {} : trimmedValue;
  }
};

const assignNestedPreferenceValue = (target: Record<string, unknown>, pathSegments: string[], value: unknown): void => {
  if (pathSegments.length === 0) {
    return;
  }

  let current = target;

  for (let index = 0; index < pathSegments.length - 1; index++) {
    const segment = pathSegments[index];
    const existingValue = current[segment];

    if (!existingValue || typeof existingValue !== 'object' || Array.isArray(existingValue)) {
      current[segment] = {};
    }

    current = current[segment] as Record<string, unknown>;
  }

  current[pathSegments[pathSegments.length - 1]] = value;
};

const UserPreferencesContent = ({ className = '' }: UserPreferencesContentProps) => {
  const { t } = useFrontendLanguage();
  const { data: preferencesData, isLoading, error, refetch } = useUserPreferencesQuery();
  const { data: servicesData } = usePreferenceServicesQuery();
  const { data: preferenceTypesData } = usePreferenceTypesQuery();

  const enabledPreferenceKeys = useMemo(() => {
    const serviceNamesFromServices = (servicesData?.services ?? [])
      .map(service => service.name)
      .filter((name): name is string => !!name && name.trim().length > 0);

    const mappedServiceKeys = mapServiceTypeNamesToPreferenceKeys(serviceNamesFromServices);
    if (mappedServiceKeys.length > 0) {
      return mappedServiceKeys;
    }

    const serviceNamesFromTypes = (preferenceTypesData?.preferenceTypes ?? [])
      .filter(type => type?.isActive !== false)
      .map(type => type?.serviceTypeName)
      .filter((name): name is string => !!name && name.trim().length > 0);

    return mapServiceTypeNamesToPreferenceKeys(serviceNamesFromTypes);
  }, [preferenceTypesData?.preferenceTypes, servicesData?.services]);

  const preferences =
    preferencesData?.preferences?.reduce(
      (acc, pref) => {
        const preferenceDisplayName = pref.preferenceTypeDisplayName?.trim();
        if (!preferenceDisplayName) {
          return acc;
        }

        const pathSegments = preferenceDisplayName
          .split('.')
          .map(segment => segment.trim())
          .filter(Boolean);
        if (pathSegments.length === 0) {
          return acc;
        }

        if (pathSegments.length === 1) {
          const rootKey = pathSegments[0];
          const parsedRootValue = parsePreferenceValue(pref.value, 'object');

          if (parsedRootValue && typeof parsedRootValue === 'object' && !Array.isArray(parsedRootValue)) {
            const existingRootValue = acc[rootKey];
            if (existingRootValue && typeof existingRootValue === 'object' && !Array.isArray(existingRootValue)) {
              acc[rootKey] = {
                ...(existingRootValue as Record<string, unknown>),
                ...(parsedRootValue as Record<string, unknown>),
              };
            } else {
              acc[rootKey] = parsedRootValue;
            }
          } else {
            acc[rootKey] = parsedRootValue;
          }

          return acc;
        }

        const rootKey = pathSegments[0];
        const nestedPath = pathSegments.slice(1);
        const parsedNestedValue = parsePreferenceValue(pref.value, 'primitive');

        const existingRootValue = acc[rootKey];
        if (!existingRootValue || typeof existingRootValue !== 'object' || Array.isArray(existingRootValue)) {
          acc[rootKey] = {};
        }

        assignNestedPreferenceValue(acc[rootKey] as Record<string, unknown>, nestedPath, parsedNestedValue);
        return acc;
      },
      {} as Record<string, unknown>
    ) || {};

  if (isLoading) {
    return <PreferencesSkeleton />;
  }

  if (error) {
    return <SectionError message={t('Unable to load preferences')} onRetry={() => refetch()} />;
  }

  return (
    <PreferencesErrorBoundary>
      <div className={`space-y-6 ${className}`}>
        <PreferencesForm initialPreferences={preferences} enabledPreferenceKeys={enabledPreferenceKeys} />
      </div>
    </PreferencesErrorBoundary>
  );
};
