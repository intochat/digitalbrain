import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { Changelog } from './Changelog';

vi.mock('app/providers', () => ({
  useFrontendLanguage: () => ({
    language: 'en',
    t: (value: string) => value,
  }),
}));

vi.mock('../content/changelog', () => ({
  changelogPageCopy: {
    eyebrow: { en: 'TripRadar changelog', ru: 'Changelog TripRadar' },
    title: { en: 'Product updates', ru: 'Обновления продукта' },
    description: { en: 'Track releases', ru: 'Следите за релизами' },
    helper: { en: 'Structured content blocks', ru: 'Структурированные блоки' },
  },
  changelogEntries: [
    {
      id: 'older-release',
      publishedAt: '2026-03-01',
      title: { en: 'Older release', ru: 'Старый релиз' },
      summary: { en: 'Older summary', ru: 'Старое описание' },
      blocks: [{ type: 'paragraph', text: { en: 'Older paragraph', ru: 'Старый абзац' } }],
    },
    {
      id: 'newer-release',
      publishedAt: '2026-04-02',
      title: { en: 'Newest release', ru: 'Новый релиз' },
      summary: { en: 'Newest summary', ru: 'Новое описание' },
      relatedLink: { label: { en: 'Open help', ru: 'Открыть помощь' }, href: '/help' },
      blocks: [
        { type: 'paragraph', text: { en: 'First paragraph for the newest release.', ru: 'Первый абзац.' } },
        {
          type: 'list',
          items: [
            { en: 'Added structured release notes.', ru: 'Добавили структурированные заметки.' },
            { en: 'Added image support.', ru: 'Добавили поддержку изображений.' },
          ],
        },
        {
          type: 'image',
          src: '/tripradar-logo-brand.png',
          alt: { en: 'Newest release cover', ru: 'Обложка релиза' },
          caption: { en: 'Release cover caption', ru: 'Подпись' },
          layout: 'cover',
        },
        { type: 'cta', label: { en: 'Open feedback', ru: 'Открыть feedback' }, href: '/feedback' },
      ],
    },
  ],
  getLocalizedText: (text: Record<'en' | 'ru', string>, language: 'en' | 'ru') => text[language],
  sortChangelogEntries: (entries: Array<{ publishedAt: string }>) =>
    [...entries].sort((left, right) => new Date(right.publishedAt).getTime() - new Date(left.publishedAt).getTime()),
}));

describe('Changelog', () => {
  it('renders release entries in reverse chronological order', () => {
    render(
      <MemoryRouter>
        <Changelog />
      </MemoryRouter>
    );

    const headings = screen.getAllByRole('heading', { level: 2 }).map(heading => heading.textContent);
    expect(headings).toEqual(['Newest release', 'Older release']);
  });

  it('renders text, list, image, related link, and CTA blocks', () => {
    render(
      <MemoryRouter>
        <Changelog />
      </MemoryRouter>
    );

    expect(screen.getByText('First paragraph for the newest release.')).toBeInTheDocument();
    expect(screen.getByText('Added structured release notes.')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Newest release cover' })).toBeInTheDocument();
    expect(screen.getByText('Release cover caption')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open help' })).toHaveAttribute('href', '/help');
    expect(screen.getByRole('link', { name: 'Open feedback' })).toHaveAttribute('href', '/feedback');
  });
});
