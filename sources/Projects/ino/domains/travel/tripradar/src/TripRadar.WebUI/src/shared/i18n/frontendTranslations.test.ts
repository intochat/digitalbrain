import { describe, expect, it } from 'vitest';
import { enTranslation, ruTranslation } from './frontendTranslations';

describe('frontendTranslations', () => {
  it('contains english translation by key', () => {
    expect(enTranslation['profile.preferences.title']).toBe('Preferences');
  });

  it('contains russian translation by key', () => {
    expect(ruTranslation['profile.preferences.title']).toBe('Настройки');
  });

  it('contains interpolation placeholders in russian translations', () => {
    expect(ruTranslation['navigation.navigateToPageAria']).toBe('Перейти на страницу «{item}»');
  });
});
