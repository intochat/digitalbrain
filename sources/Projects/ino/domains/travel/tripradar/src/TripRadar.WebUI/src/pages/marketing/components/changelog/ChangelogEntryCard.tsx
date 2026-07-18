import type { FrontendLanguage } from 'shared/i18n';
import { getLocalizedText, type ChangelogEntry } from '../../content/changelog';
import { ChangelogBlockRenderer } from './ChangelogBlockRenderer';

interface ChangelogEntryCardProps {
  entry: ChangelogEntry;
  language: FrontendLanguage;
}

const localeByLanguage: Record<FrontendLanguage, string> = {
  en: 'en-US',
  ru: 'ru-RU',
};

const formatPublishedDate = (publishedAt: string, language: FrontendLanguage): string => {
  return new Intl.DateTimeFormat(localeByLanguage[language], {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(publishedAt));
};

export const ChangelogEntryCard = ({ entry, language }: ChangelogEntryCardProps) => {
  const title = getLocalizedText(entry.title, language);
  const summary = getLocalizedText(entry.summary, language);
  const publishedDate = formatPublishedDate(entry.publishedAt, language);

  return (
    <article className="relative py-10 first:pt-0" aria-labelledby={`changelog-entry-${entry.id}`}>
      {/* Date — positioned to the left on large screens, inline on mobile */}
      <time
        dateTime={entry.publishedAt}
        className="block text-xs text-content-muted dark:text-content-muted-dark mb-2 lg:absolute lg:right-full lg:mr-10 lg:mt-1.5 lg:mb-0 lg:whitespace-nowrap"
      >
        {publishedDate}
      </time>

      <h2
        id={`changelog-entry-${entry.id}`}
        className="text-xl font-semibold text-content dark:text-content-dark sm:text-2xl"
      >
        {title}
      </h2>

      <p className="mt-2 text-sm leading-relaxed text-content-secondary dark:text-content-secondary-dark">{summary}</p>

      <div className="mt-6 flex flex-col gap-4">
        {entry.blocks.map((block, index) => (
          <ChangelogBlockRenderer key={`${entry.id}-${block.type}-${index}`} block={block} language={language} />
        ))}
      </div>
    </article>
  );
};
