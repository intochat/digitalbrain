export const FRONTEND_LANGUAGES = ['en', 'ru'] as const;

export type FrontendLanguage = (typeof FRONTEND_LANGUAGES)[number];

export const DEFAULT_FRONTEND_LANGUAGE: FrontendLanguage = 'en';

const frontendLanguageSet: Set<string> = new Set(FRONTEND_LANGUAGES);
const frontendLanguageAliases: Readonly<Record<string, FrontendLanguage>> = {
  en: 'en',
  'en-us': 'en',
  'en-gb': 'en',
  english: 'en',
  ru: 'ru',
  'ru-ru': 'ru',
  russian: 'ru',
  russianlanguage: 'ru',
};

export const resolveFrontendLanguage = (languageCode: string | null | undefined): FrontendLanguage => {
  const normalizedCode = languageCode?.trim().toLowerCase();
  if (!normalizedCode) {
    return DEFAULT_FRONTEND_LANGUAGE;
  }

  const aliasLanguage = frontendLanguageAliases[normalizedCode];
  if (aliasLanguage) {
    return aliasLanguage;
  }

  if (frontendLanguageSet.has(normalizedCode)) {
    return normalizedCode as FrontendLanguage;
  }

  const normalizedBaseCode = normalizedCode.split('-')[0];
  const baseAliasLanguage = frontendLanguageAliases[normalizedBaseCode];
  if (baseAliasLanguage) {
    return baseAliasLanguage;
  }

  if (frontendLanguageSet.has(normalizedBaseCode)) {
    return normalizedBaseCode as FrontendLanguage;
  }

  return DEFAULT_FRONTEND_LANGUAGE;
};
