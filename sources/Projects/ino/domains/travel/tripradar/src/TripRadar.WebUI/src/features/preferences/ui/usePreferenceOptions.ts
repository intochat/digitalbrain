import { useMemo } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { usePortalCurrenciesQuery } from 'entities/portal/api/usePortalCurrenciesQuery';
import { usePortalLanguagesQuery } from 'entities/portal/api/usePortalLanguagesQuery';
import { createCurrencyOption } from 'shared/lib/currency/currencyPresentation';

export const usePreferenceOptions = () => {
  const { language } = useFrontendLanguage();
  const { data: currenciesData, isLoading: isLoadingCurrencies } = usePortalCurrenciesQuery();
  const { data: languagesData, isLoading: isLoadingLanguages } = usePortalLanguagesQuery();

  const currencyOptions = useMemo(() => {
    return (
      currenciesData?.currencies.map(currency => ({
        ...createCurrencyOption(currency, language),
        isLabelTranslated: true,
      })) ?? []
    );
  }, [currenciesData, language]);

  const languageOptions = useMemo(() => {
    return (
      languagesData?.languages.map(l => ({
        value: l.languageCode,
        label: l.languageName,
      })) ?? []
    );
  }, [languagesData]);

  return {
    currencyOptions,
    languageOptions,
    isLoading: isLoadingCurrencies || isLoadingLanguages,
  };
};
