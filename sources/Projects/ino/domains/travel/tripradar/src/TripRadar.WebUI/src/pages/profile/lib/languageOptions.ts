import type { components } from 'shared/api/generated-types';

export interface LanguageOption {
  value: string;
  label: string;
  countryCode?: string;
}

type LanguageResponse = components['schemas']['LanguageResponse'];

const LANGUAGE_COUNTRY_MAP: Record<string, string> = {
  ar: 'SA',
  de: 'DE',
  en: 'GB',
  es: 'ES',
  fr: 'FR',
  hi: 'IN',
  it: 'IT',
  ja: 'JP',
  ko: 'KR',
  pt: 'PT',
  ru: 'RU',
  zh: 'CN',
};

const LANGUAGE_NAME_FALLBACK_MAP: Record<string, string> = {
  ar: 'Arabic',
  de: 'German',
  en: 'English',
  es: 'Spanish',
  fr: 'French',
  hi: 'Hindi',
  it: 'Italian',
  ja: 'Japanese',
  ko: 'Korean',
  pt: 'Portuguese',
  ru: 'Russian',
  zh: 'Chinese',
};

const toTitleCase = (value: string): string => {
  const trimmed = value.trim();
  if (!trimmed) {
    return trimmed;
  }

  return trimmed
    .split(/\s+/)
    .filter(Boolean)
    .map(word => word[0].toUpperCase() + word.slice(1).toLowerCase())
    .join(' ');
};

const cleanLanguageName = (languageName: string): string => {
  const trimmed = languageName.trim();
  if (!trimmed) {
    return trimmed;
  }

  // Some datasets include prefixes like "DE German" or "GB English".
  return trimmed.replace(/^[A-Z]{2,3}\s+/, '');
};

const getCountryCode = (languageCode: string): string | undefined => {
  return LANGUAGE_COUNTRY_MAP[languageCode.trim().toLowerCase()];
};

const formatLanguageLabel = (languageCode: string, languageName?: string | null): string => {
  const normalizedCode = languageCode.trim().toLowerCase();
  const normalizedName = languageName?.trim();
  const resolvedName =
    normalizedName && normalizedName.length > 0
      ? cleanLanguageName(normalizedName)
      : LANGUAGE_NAME_FALLBACK_MAP[normalizedCode] || normalizedCode.toUpperCase();

  return toTitleCase(resolvedName);
};

const DEFAULT_LANGUAGE_OPTIONS: LanguageOption[] = [
  {
    value: 'en',
    label: formatLanguageLabel('en', 'English'),
    countryCode: getCountryCode('en'),
  },
  {
    value: 'ru',
    label: formatLanguageLabel('ru', 'Russian'),
    countryCode: getCountryCode('ru'),
  },
];

const normalizeLanguageOptions = (options: LanguageOption[]): LanguageOption[] => {
  const optionMap: Map<string, LanguageOption> = new Map<string, LanguageOption>();

  options.forEach(option => {
    if (!option.value || !option.label) {
      return;
    }

    optionMap.set(option.value, option);
  });

  return Array.from(optionMap.values()).sort((left, right) => left.label.localeCompare(right.label));
};

export const buildLanguageOptions = (languages: LanguageResponse[] | undefined): LanguageOption[] => {
  const languageOptions: LanguageOption[] = (languages ?? []).map(language => ({
    value: language.languageCode.trim().toLowerCase(),
    label: formatLanguageLabel(language.languageCode, language.languageName),
    countryCode: getCountryCode(language.languageCode),
  }));

  return normalizeLanguageOptions([...DEFAULT_LANGUAGE_OPTIONS, ...languageOptions]);
};
