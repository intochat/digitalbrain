import { describe, expect, it } from 'vitest';
import { buildLanguageOptions } from './languageOptions';

describe('buildLanguageOptions', () => {
  it('uses backend languages with capitalized names', () => {
    const options = buildLanguageOptions([
      { languageCode: 'ru', languageName: 'russian' },
      { languageCode: 'en', languageName: 'english' },
    ]);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });

  it('falls back to required frontend languages when backend languages are missing', () => {
    const options = buildLanguageOptions(undefined);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });

  it('keeps required frontend languages even if backend provides a subset', () => {
    const options = buildLanguageOptions([{ languageCode: 'en', languageName: 'English' }]);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });

  it('uses fallback language name when language name is missing', () => {
    const options = buildLanguageOptions([{ languageCode: 'ru', languageName: '' }]);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });

  it('normalizes language codes to lowercase', () => {
    const options = buildLanguageOptions([{ languageCode: 'EN', languageName: 'english' }]);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });

  it('removes short uppercase prefixes from language names', () => {
    const options = buildLanguageOptions([
      { languageCode: 'de', languageName: 'DE German' },
      { languageCode: 'en', languageName: 'GB English' },
    ]);

    expect(options).toEqual([
      { value: 'en', label: 'English', countryCode: 'GB' },
      { value: 'de', label: 'German', countryCode: 'DE' },
      { value: 'ru', label: 'Russian', countryCode: 'RU' },
    ]);
  });
});
