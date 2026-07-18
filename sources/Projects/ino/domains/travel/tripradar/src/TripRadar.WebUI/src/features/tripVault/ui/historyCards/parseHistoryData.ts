/**
 * Shared utilities for parsing and displaying trip history data.
 */

type JsonObject = Record<string, unknown>;

const toObject = (value: unknown): JsonObject | null => {
  if (!value || typeof value !== 'object') {
    return null;
  }

  if (Array.isArray(value)) {
    return { items: value };
  }

  return value as JsonObject;
};

/** Add enough closing brackets/braces to balance the JSON string. */
const closeJson = (str: string): string => {
  let result = str;
  const stack: string[] = [];
  let inString = false;
  let escape = false;

  for (const ch of result) {
    if (escape) {
      escape = false;
      continue;
    }

    if (ch === '\\') {
      escape = true;
      continue;
    }

    if (ch === '"') {
      inString = !inString;
      continue;
    }

    if (inString) continue;

    if (ch === '{') stack.push('}');
    else if (ch === '[') stack.push(']');
    else if (ch === '}' || ch === ']') stack.pop();
  }

  // If we're inside a string, close it first
  if (inString) {
    result += '"';
  }

  // Close any remaining open brackets
  while (stack.length > 0) {
    result += stack.pop();
  }

  return result;
};

const attemptParse = (str: string): JsonObject | null => {
  try {
    const parsed = toObject(JSON.parse(str));
    if (parsed) {
      return parsed;
    }
  } catch {
    // not valid
  }
  return null;
};

const decodeEscapedJson = (value: string): string | null => {
  try {
    const escaped = value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
    return JSON.parse(`"${escaped}"`) as string;
  } catch {
    return null;
  }
};

const buildJsonCandidates = (raw: string): string[] => {
  const candidates = new Set<string>();
  const trimmed = raw.trim();
  if (!trimmed) {
    return [];
  }

  candidates.add(trimmed);

  if (trimmed.includes('\\u0022') || trimmed.includes('\\\\u0022') || trimmed.includes('\\"')) {
    candidates.add(
      trimmed
        .replace(/\\\\u0022/gi, '"')
        .replace(/\\u0022/gi, '"')
        .replace(/\\"/g, '"')
    );
  }

  const decoded = decodeEscapedJson(trimmed);
  if (decoded) {
    candidates.add(decoded);
  }

  try {
    const parsed = JSON.parse(trimmed);
    if (typeof parsed === 'string' && parsed.trim().length > 0) {
      candidates.add(parsed.trim());
    }
  } catch {
    // ignore
  }

  return [...candidates];
};

const tryParseWithBoundaryRepair = (raw: string): JsonObject | null => {
  const direct = attemptParse(raw);
  if (direct) {
    return direct;
  }

  for (let i = raw.length; i > 0; i--) {
    const ch = raw[i - 1];

    if (ch === ',' || ch === '{' || ch === '[') {
      const slice = raw.slice(0, ch === ',' ? i - 1 : i);
      const repaired = closeJson(slice);
      const repairedParsed = attemptParse(repaired);
      if (repairedParsed) {
        return repairedParsed;
      }
    }
  }

  return attemptParse(closeJson(raw));
};

/**
 * Try to parse a truncated JSON string.
 */
const tryParseTruncatedJson = (raw: string): JsonObject | null => {
  const candidates = buildJsonCandidates(raw);
  for (const candidate of candidates) {
    const parsed = tryParseWithBoundaryRepair(candidate);
    if (parsed) {
      return parsed;
    }
  }

  return null;
};

export const isTruncatedWrapperPayload = (data: JsonObject): boolean => {
  return data.truncated === true && typeof data.preview === 'string';
};

/**
 * Safely parses a JSON string.
 * Automatically handles the backend's "truncated" wrapper object.
 */
export const safeParse = (json: string | null | undefined): JsonObject | null => {
  if (!json) {
    return null;
  }

  try {
    const parsed = toObject(JSON.parse(json));
    if (!parsed) {
      return null;
    }

    if (isTruncatedWrapperPayload(parsed)) {
      const unwrapped = tryParseTruncatedJson(parsed.preview as string);
      if (unwrapped) {
        return unwrapped;
      }

      return parsed;
    }

    return parsed;
  } catch {
    return tryParseTruncatedJson(json);
  }
};

/** Extract a string value from a nested path like "searchParameters.departureId". */
export const getString = (obj: Record<string, unknown>, ...keys: string[]): string | null => {
  let current: unknown = obj;
  for (const key of keys) {
    if (current == null || typeof current !== 'object') {
      return null;
    }
    current = (current as Record<string, unknown>)[key];
  }
  return typeof current === 'string' ? current : null;
};

/** Extract a number value from a nested path. */
export const getNumber = (obj: Record<string, unknown>, ...keys: string[]): number | null => {
  let current: unknown = obj;
  for (const key of keys) {
    if (current == null || typeof current !== 'object') {
      return null;
    }
    current = (current as Record<string, unknown>)[key];
  }
  const val = Number(current);
  return !isNaN(val) ? val : null;
};

/** Extract an array from a nested path. */
export const getArray = (obj: Record<string, unknown>, ...keys: string[]): unknown[] | null => {
  let current: unknown = obj;
  for (const key of keys) {
    if (current == null || typeof current !== 'object') {
      return null;
    }
    current = (current as Record<string, unknown>)[key];
  }
  return Array.isArray(current) ? current : null;
};

/** Extract a nested object from a path. */
export const getObject = (obj: Record<string, unknown>, ...keys: string[]): Record<string, unknown> | null => {
  let current: unknown = obj;
  for (const key of keys) {
    if (current == null || typeof current !== 'object') {
      return null;
    }
    current = (current as Record<string, unknown>)[key];
  }
  if (current && typeof current === 'object' && !Array.isArray(current)) {
    return current as Record<string, unknown>;
  }
  return null;
};

/** Format a number as a star rating, e.g. "4.5 ★". */
export const formatRating = (rating: number | null): string => {
  if (rating == null) {
    return '—';
  }
  return `${rating.toFixed(1)} ★`;
};

/** Format a price with currency symbol. */
export const formatPrice = (price: number | null, currency?: string | null): string => {
  if (price == null) {
    return '—';
  }
  if (currency) {
    try {
      return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(price);
    } catch {
      // Fall through
    }
  }
  return `$${price.toLocaleString()}`;
};

/** Format minutes into hours and minutes. */
export const formatDuration = (minutes: number | null): string => {
  if (minutes == null) {
    return '—';
  }
  const hours = Math.floor(minutes / 60);
  const mins = minutes % 60;
  if (hours === 0) {
    return `${mins}m`;
  }
  return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
};

/** Truncate long text with ellipsis. */
export const truncateText = (text: string, maxLength: number): string => {
  if (text.length <= maxLength) {
    return text;
  }
  return `${text.slice(0, maxLength - 1)}…`;
};

const formatUnknownValue = (value: unknown): string => {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'object') {
    if (Array.isArray(value)) return `Array(${value.length})`;
    const keys = Object.keys(value as object);
    return `${keys.length} fields`;
  }
  return String(value);
};

const SKIP_HIGHLIGHT_KEYS = new Set([
  'error',
  'search_metadata',
  'search_parameters',
  'search_information',
  'pagination',
  'serpapi_pagination',
]);

export const extractHighlights = (
  data: Record<string, unknown>,
  maxItems = 6
): Array<{ key: string; value: string }> => {
  return Object.entries(data)
    .filter(([key]) => !SKIP_HIGHLIGHT_KEYS.has(key))
    .slice(0, maxItems)
    .map(([key, value]) => ({
      key: key.replace(/_/g, ' ').replace(/\b\w/g, ch => ch.toUpperCase()),
      value: formatUnknownValue(value),
    }));
};
