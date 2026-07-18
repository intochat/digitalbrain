import { useMemo } from 'react';
import { useFrontendLanguage } from 'app/providers';
import { ChangelogEntryCard, ChangelogHero, ChangelogTimeline } from '../components/changelog';
import { changelogEntries, changelogPageCopy, getLocalizedText, sortChangelogEntries } from '../content/changelog';

export const Changelog = () => {
  const { language, t } = useFrontendLanguage();
  const entries = useMemo(() => sortChangelogEntries(changelogEntries), []);

  return (
    <>
      <a
        href="#changelog-content"
        className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:px-4 focus:py-2 focus:bg-button dark:focus:bg-button-dark focus:text-button-text dark:focus:text-button-text-dark focus:rounded-xl focus:shadow-lg focus:ring-2 focus:ring-content/10 focus:ring-offset-2 focus:ring-offset-surface dark:focus:ring-offset-surface-dark"
        aria-label={t('Skip to main content')}
      >
        {t('Skip to main content')}
      </a>

      <main
        id="changelog-content"
        className="flex-1 bg-surface dark:bg-surface-dark"
        role="main"
        aria-label={getLocalizedText(changelogPageCopy.title, language)}
      >
        <ChangelogHero language={language} />

        <ChangelogTimeline>
          {entries.map(entry => (
            <ChangelogEntryCard key={entry.id} entry={entry} language={language} />
          ))}
        </ChangelogTimeline>
      </main>
    </>
  );
};
