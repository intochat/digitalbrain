import { Link } from 'react-router-dom';
import type { FrontendLanguage } from 'shared/i18n';
import { cn } from 'shared/lib/utils';
import { getLocalizedText, type ChangelogBlock } from '../../content/changelog';

interface ChangelogBlockRendererProps {
  block: ChangelogBlock;
  language: FrontendLanguage;
}

const ctaClassName = [
  'inline-flex w-fit items-center gap-1.5 rounded-md border border-outline px-3 py-1.5',
  'text-sm text-content transition-colors hover:bg-surface-accent',
  'dark:border-outline-dark dark:text-content-dark dark:hover:bg-surface-accent-dark',
].join(' ');

export const ChangelogBlockRenderer = ({ block, language }: ChangelogBlockRendererProps) => {
  switch (block.type) {
    case 'paragraph':
      return (
        <p className="text-sm leading-relaxed text-content-secondary dark:text-content-secondary-dark">
          {getLocalizedText(block.text, language)}
        </p>
      );
    case 'list':
      return (
        <ul className="list-disc space-y-1.5 pl-4 text-sm leading-relaxed text-content-secondary marker:text-content-muted/40 dark:text-content-secondary-dark dark:marker:text-content-muted-dark/40">
          {block.items.map(item => {
            const localizedItem = getLocalizedText(item, language);
            return <li key={localizedItem}>{localizedItem}</li>;
          })}
        </ul>
      );
    case 'image': {
      const caption = block.caption ? getLocalizedText(block.caption, language) : null;
      const alt = getLocalizedText(block.alt, language);

      return (
        <figure className="flex flex-col gap-2">
          <img
            src={block.src}
            alt={alt}
            className={cn(
              'w-full rounded-lg object-cover',
              block.layout === 'cover' ? 'max-h-[28rem]' : 'max-h-[20rem]'
            )}
          />
          {caption ? (
            <figcaption className="text-xs text-content-muted dark:text-content-muted-dark">{caption}</figcaption>
          ) : null}
        </figure>
      );
    }
    case 'cta': {
      const label = getLocalizedText(block.label, language);

      if (block.external) {
        return (
          <a href={block.href} target="_blank" rel="noreferrer" className={ctaClassName}>
            {label}
            <span aria-hidden="true" className="text-xs text-content-muted dark:text-content-muted-dark">
              ↗
            </span>
          </a>
        );
      }

      return (
        <Link to={block.href} className={ctaClassName}>
          {label}
        </Link>
      );
    }
    default:
      return null;
  }
};
