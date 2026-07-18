import { useTranslation } from 'react-i18next';
import { resolveFrontendLanguage } from 'shared/i18n';

export const useFrontendLanguage = () => {
  const { t, i18n } = useTranslation();
  const language = resolveFrontendLanguage(i18n.resolvedLanguage ?? i18n.language);

  return {
    language,
    t,
  };
};
