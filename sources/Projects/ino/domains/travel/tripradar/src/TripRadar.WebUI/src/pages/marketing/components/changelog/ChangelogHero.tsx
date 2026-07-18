import type { FrontendLanguage } from 'shared/i18n';
import { changelogPageCopy, getLocalizedText } from '../../content/changelog';

interface ChangelogHeroProps {
  language: FrontendLanguage;
}

export const ChangelogHero = ({ language }: ChangelogHeroProps) => {
  const title = getLocalizedText(changelogPageCopy.eyebrow, language);

  return (
    <div className="mx-auto max-w-2xl px-4 pt-24 sm:px-6 sm:pt-28 lg:px-8 lg:pt-32">
      <p className="text-xs font-medium uppercase tracking-widest text-content-muted dark:text-content-muted-dark">{title}</p>
    </div>
  );
};
