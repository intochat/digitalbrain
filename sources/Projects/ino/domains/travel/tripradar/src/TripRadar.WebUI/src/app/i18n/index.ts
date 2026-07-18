import i18next, { type Resource } from 'i18next';
import { initReactI18next } from 'react-i18next';
import { DEFAULT_FRONTEND_LANGUAGE, resolveFrontendLanguage, enTranslation, ruTranslation } from 'shared/i18n';

const FRONTEND_LANGUAGE_STORAGE_KEY = 'tripradar.frontendLanguage';

const resolveInitialFrontendLanguage = () => {
  if (typeof window === 'undefined') {
    return DEFAULT_FRONTEND_LANGUAGE;
  }

  const storedLanguage = resolveFrontendLanguage(window.localStorage.getItem(FRONTEND_LANGUAGE_STORAGE_KEY));
  if (storedLanguage !== DEFAULT_FRONTEND_LANGUAGE || window.localStorage.getItem(FRONTEND_LANGUAGE_STORAGE_KEY)) {
    return storedLanguage;
  }

  const browserLanguage = window.navigator.language?.split('-')[0];
  return resolveFrontendLanguage(browserLanguage);
};

const resources: Resource = {
  en: {
    translation: enTranslation,
  },
  ru: {
    translation: ruTranslation,
  },
};

if (!i18next.isInitialized) {
  void i18next.use(initReactI18next).init({
    resources,
    lng: resolveInitialFrontendLanguage(),
    fallbackLng: DEFAULT_FRONTEND_LANGUAGE,
    supportedLngs: ['en', 'ru'],
    interpolation: {
      escapeValue: false,
      prefix: '{',
      suffix: '}',
    },
    keySeparator: false,
    nsSeparator: false,
    returnNull: false,
  });
}

i18next.on('languageChanged', language => {
  if (typeof window === 'undefined') {
    return;
  }

  const resolvedLanguage = resolveFrontendLanguage(language);
  window.localStorage.setItem(FRONTEND_LANGUAGE_STORAGE_KEY, resolvedLanguage);
});

export const frontendI18n = i18next;
