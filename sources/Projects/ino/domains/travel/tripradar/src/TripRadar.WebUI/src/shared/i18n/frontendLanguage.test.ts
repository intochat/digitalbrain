import { describe, expect, it } from 'vitest';
import { resolveFrontendLanguage } from './frontendLanguage';

describe('resolveFrontendLanguage', () => {
  it('returns russian language for ru code', () => {
    expect(resolveFrontendLanguage('ru')).toBe('ru');
  });

  it('normalizes casing and whitespace', () => {
    expect(resolveFrontendLanguage(' RU ')).toBe('ru');
  });

  it('supports locale variants from browser and backend', () => {
    expect(resolveFrontendLanguage('ru-RU')).toBe('ru');
    expect(resolveFrontendLanguage('en-US')).toBe('en');
  });

  it('supports language name aliases', () => {
    expect(resolveFrontendLanguage('Russian')).toBe('ru');
    expect(resolveFrontendLanguage('English')).toBe('en');
  });

  it('falls back to english for unsupported frontend language', () => {
    expect(resolveFrontendLanguage('de')).toBe('en');
  });

  it('falls back to english for empty value', () => {
    expect(resolveFrontendLanguage(undefined)).toBe('en');
  });
});
