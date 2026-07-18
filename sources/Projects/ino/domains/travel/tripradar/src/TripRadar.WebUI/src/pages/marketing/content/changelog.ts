import type { FrontendLanguage } from 'shared/i18n';

export type LocalizedText = Record<FrontendLanguage, string>;

export interface ChangelogLink {
  label: LocalizedText;
  href: string;
  external?: boolean;
}

export interface ParagraphBlock {
  type: 'paragraph';
  text: LocalizedText;
}

export interface ListBlock {
  type: 'list';
  items: LocalizedText[];
}

export interface ImageBlock {
  type: 'image';
  src: string;
  alt: LocalizedText;
  caption?: LocalizedText;
  layout?: 'cover' | 'inline';
}

export interface CtaBlock {
  type: 'cta';
  label: LocalizedText;
  href: string;
  external?: boolean;
}

export type ChangelogBlock = ParagraphBlock | ListBlock | ImageBlock | CtaBlock;

export interface ChangelogEntry {
  id: string;
  publishedAt: string;
  title: LocalizedText;
  summary: LocalizedText;
  blocks: ChangelogBlock[];
  relatedLink?: ChangelogLink;
}

export interface ChangelogPageCopy {
  eyebrow: LocalizedText;
  title: LocalizedText;
  description: LocalizedText;
  helper: LocalizedText;
}

export const changelogPageCopy: ChangelogPageCopy = {
  eyebrow: {
    en: 'Changelog',
    ru: 'Журнал изменений',
  },
  title: {
    en: 'Product updates, fixes, and release notes in one place.',
    ru: 'Обновления продукта, исправления и release notes в одном месте.',
  },
  description: {
    en: 'Follow what changed in TripRadar without digging through support threads or release messages.',
    ru: 'Следите за изменениями в TripRadar без поиска по support-тредам и разрозненным релизным сообщениям.',
  },
  helper: {
    en: 'Each entry can include structured text, bullet lists, images, and links so new updates are easy to publish and easy to scan.',
    ru: 'Каждая запись поддерживает структурированный текст, списки, изображения и ссылки, чтобы новые обновления было легко публиковать и читать.',
  },
};

export const changelogEntries: ChangelogEntry[] = [
  {
    id: 'public-changelog-launch',
    publishedAt: '2026-04-04',
    title: {
      en: 'Public changelog launched',
      ru: 'Запущен публичный changelog',
    },
    summary: {
      en: 'We created a public release notes page so updates, improvements, and fixes are easier to follow from the website.',
      ru: 'Мы запустили публичную страницу release notes, чтобы обновления, улучшения и исправления было проще отслеживать прямо на сайте.',
    },
    blocks: [
      {
        type: 'image',
        src: '/changelog-launch.svg',
        alt: {
          en: 'TripRadar changelog launch',
          ru: 'Запуск changelog TripRadar',
        },
        layout: 'cover',
      },
      {
        type: 'list',
        items: [
          {
            en: 'Reverse-chronological release feed for public updates.',
            ru: 'Лента публичных обновлений в обратном хронологическом порядке.',
          },
          {
            en: 'Structured content blocks for paragraphs, bullet lists, images, and calls to action.',
            ru: 'Структурированные контентные блоки для абзацев, списков, изображений и CTA.',
          },
          {
            en: 'Dual-language content support for English and Russian entries.',
            ru: 'Поддержка двуязычного контента для английских и русских записей.',
          },
        ],
      },
      {
        type: 'paragraph',
        text: {
          en: 'Future entries should document what changed, why it matters, and where users can learn more or send feedback.',
          ru: 'Следующие записи должны объяснять, что изменилось, почему это важно и где пользователь может узнать детали или оставить отзыв.',
        },
      },
    ],
  },
];

export const getLocalizedText = (text: LocalizedText, language: FrontendLanguage): string => {
  return text[language];
};

export const sortChangelogEntries = (entries: ChangelogEntry[]): ChangelogEntry[] => {
  return [...entries].sort(
    (left, right) => new Date(right.publishedAt).getTime() - new Date(left.publishedAt).getTime()
  );
};
