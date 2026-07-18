import { useEffect, type ReactNode } from 'react';
import { frontendI18n } from 'app/i18n';
import { useProfileQuery } from 'entities/user/api';
import { resolveFrontendLanguage } from 'shared/i18n';
import { useAuthStore } from 'shared/store/auth';

interface FrontendLanguageProviderProps {
  children: ReactNode;
}

export const FrontendLanguageProvider = ({ children }: FrontendLanguageProviderProps) => {
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  const {
    data: profile,
    isLoading: isProfileLoading,
    isFetching: isProfileFetching,
    isError: isProfileError,
  } = useProfileQuery({ enabled: isAuthenticated });
  const hasProfile = Boolean(profile);
  const profileLanguageCode = profile?.languageCode;

  useEffect(() => {
    if (isAuthenticated && !hasProfile && !isProfileError) {
      return;
    }

    const resolvedLanguage = resolveFrontendLanguage(
      profileLanguageCode ?? frontendI18n.resolvedLanguage ?? frontendI18n.language
    );

    if (frontendI18n.resolvedLanguage === resolvedLanguage) {
      return;
    }

    void frontendI18n.changeLanguage(resolvedLanguage);
  }, [hasProfile, isAuthenticated, isProfileError, profileLanguageCode]);

  const shouldRenderLoadingState =
    isAuthenticated && !hasProfile && !isProfileError && (isProfileLoading || isProfileFetching);

  if (shouldRenderLoadingState) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-surface dark:bg-surface-dark">
        <div
          className="animate-spin rounded-full h-12 w-12 border-4 border-outline/30 dark:border-outline-dark/30 border-t-content-secondary dark:border-t-content-secondary-dark"
          role="status"
          aria-label="Loading"
        >
          <span className="sr-only">{frontendI18n.t('Loading...')}</span>
        </div>
      </div>
    );
  }

  return <>{children}</>;
};
