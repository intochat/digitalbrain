import { describe, expect, it } from 'vitest';
import { isTruncatedWrapperPayload, safeParse } from './parseHistoryData';

describe('safeParse', () => {
  it('parses truncated preview payload with escaped quotes', () => {
    const summary = JSON.stringify({
      truncated: true,
      originalLength: 10432,
      preview:
        '{\\u0022search_metadata\\u0022:{\\u0022status\\u0022:\\u0022Success\\u0022},\\u0022local_results\\u0022:[{\\u0022title\\u0022:\\u0022Cafe Roma\\u0022}]',
    });

    const parsed = safeParse(summary);

    expect(parsed).not.toBeNull();
    expect(isTruncatedWrapperPayload(parsed!)).toBe(false);
    expect(Array.isArray(parsed)).toBe(false);
  });

  it('unwraps a double-encoded preview payload', () => {
    const summary = JSON.stringify({
      truncated: true,
      originalLength: 9000,
      preview: JSON.stringify('{"organic_results":[{"title":"Result 1"}]}'),
    });

    const parsed = safeParse(summary);
    const organicResults = (parsed as Record<string, unknown>).organic_results as Array<Record<string, unknown>>;

    expect(parsed).not.toBeNull();
    expect(Array.isArray(organicResults)).toBe(true);
    expect(organicResults[0].title).toBe('Result 1');
  });

  it('returns the truncated wrapper when preview recovery fails', () => {
    const summary = JSON.stringify({
      truncated: true,
      originalLength: 9000,
      preview: '{not valid json payload',
    });

    const parsed = safeParse(summary);

    expect(parsed).not.toBeNull();
    expect(parsed).toEqual(expect.any(Object));
  });
});
