import { env } from 'shared/config';

type TelemetryEventType = 'app_start' | 'page_view' | 'event' | 'frontend_error' | 'unhandled_rejection';
type EventStage = 'acquisition' | 'activation' | 'retention' | 'revenue';
type UserState = 'anon' | 'signed_up' | 'activated' | 'paid';
export type TelemetryEventName =
  | 'visit'
  | 'cta_click'
  | 'signup_start'
  | 'signup_complete'
  | 'telegram_connect'
  | 'first_trip_request'
  | 'first_saved_trip'
  | 'checkout_start'
  | 'paid_conversion'
  | (string & {});

interface AttributionData {
  utm_source?: string;
  utm_medium?: string;
  utm_campaign?: string;
  utm_content?: string;
  utm_term?: string;
}

interface TelemetryMetadata extends Record<string, unknown> {
  event_name?: TelemetryEventName;
  attribution?: AttributionData;
  event_stage?: EventStage;
  user_state?: UserState;
  experiment_id?: string;
  variant?: string;
}

interface TrackEventOptions {
  stage?: EventStage;
  userState?: UserState;
  attribution?: AttributionData;
  experimentId?: string;
  variant?: string;
}

interface TelemetryPayload {
  type: TelemetryEventType;
  timestamp: string;
  path: string;
  message?: string;
  stack?: string;
  metadata?: TelemetryMetadata;
}

const telemetryEnabled = env.TELEMETRY_ENABLED.trim().toLowerCase() === 'true';
const telemetryEndpoint = env.FRONTEND_ERROR_INGEST_URL.trim();
const analyticsDebugEnabled = env.ANALYTICS_DEBUG.trim().toLowerCase() === 'true';
const attributionStorageKey = 'tripradar.attribution.v1';

let isInitialized = false;

const canSendTelemetry = (): boolean => telemetryEnabled && telemetryEndpoint.length > 0;

const debugLog = (message: string, payload: TelemetryPayload): void => {
  if (!analyticsDebugEnabled) {
    return;
  }

  console.debug(`[telemetry] ${message}`, payload);
};

const readStoredAttribution = (): AttributionData | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    const rawValue = window.sessionStorage.getItem(attributionStorageKey);
    if (!rawValue) {
      return null;
    }

    const parsedValue = JSON.parse(rawValue) as AttributionData;
    if (typeof parsedValue !== 'object' || parsedValue === null) {
      return null;
    }

    return parsedValue;
  } catch {
    return null;
  }
};

const writeStoredAttribution = (attribution: AttributionData): void => {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.sessionStorage.setItem(attributionStorageKey, JSON.stringify(attribution));
  } catch {
    // no-op: telemetry must never break app flow
  }
};

const extractAttributionFromSearch = (search: string): AttributionData | null => {
  const searchParams = new URLSearchParams(search);
  const value = (key: string): string | undefined => {
    const parameterValue = searchParams.get(key)?.trim();
    return parameterValue ? parameterValue : undefined;
  };

  const attribution: AttributionData = {
    utm_source: value('utm_source'),
    utm_medium: value('utm_medium'),
    utm_campaign: value('utm_campaign'),
    utm_content: value('utm_content'),
    utm_term: value('utm_term'),
  };

  const hasAttributionValue = Object.values(attribution).some(Boolean);
  return hasAttributionValue ? attribution : null;
};

const mergeAttribution = (base: AttributionData | null, incoming: AttributionData | null): AttributionData | null => {
  if (!base && !incoming) {
    return null;
  }

  return {
    ...(base ?? {}),
    ...(incoming ?? {}),
  };
};

const captureAttributionFromUrl = (): AttributionData | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  const storedAttribution = readStoredAttribution();
  const urlAttribution = extractAttributionFromSearch(window.location.search);
  const mergedAttribution = mergeAttribution(storedAttribution, urlAttribution);
  if (mergedAttribution) {
    writeStoredAttribution(mergedAttribution);
  }

  return mergedAttribution;
};

const getCurrentAttribution = (): AttributionData | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  return captureAttributionFromUrl() ?? readStoredAttribution();
};

const enrichMetadataWithAttribution = (metadata?: TelemetryMetadata): TelemetryMetadata | undefined => {
  const attribution = getCurrentAttribution();
  if (!attribution) {
    return metadata;
  }

  if (!metadata) {
    return { attribution };
  }

  return {
    ...metadata,
    attribution: metadata.attribution ?? attribution,
  };
};

const sendTelemetry = (payload: TelemetryPayload): void => {
  if (!canSendTelemetry()) {
    return;
  }

  const payloadWithAttribution: TelemetryPayload = {
    ...payload,
    metadata: enrichMetadataWithAttribution(payload.metadata),
  };

  debugLog('sending payload', payloadWithAttribution);
  const body = JSON.stringify(payloadWithAttribution);

  if (navigator.sendBeacon) {
    const blob = new Blob([body], { type: 'application/json' });
    navigator.sendBeacon(telemetryEndpoint, blob);
    return;
  }

  void fetch(telemetryEndpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
    keepalive: true,
    credentials: 'omit',
  });
};

const resolveStageByPath = (path: string): EventStage => {
  const normalizedPath = path.split('?')[0].split('#')[0];

  if (
    normalizedPath.startsWith('/signup') ||
    normalizedPath.startsWith('/signin') ||
    normalizedPath.startsWith('/email-sent') ||
    normalizedPath.startsWith('/email-confirmed') ||
    normalizedPath.startsWith('/auth/') ||
    normalizedPath.startsWith('/forgot-password') ||
    normalizedPath.startsWith('/reset-password')
  ) {
    return 'activation';
  }

  if (
    normalizedPath.startsWith('/pricing') ||
    normalizedPath.startsWith('/payment') ||
    normalizedPath.startsWith('/subscription')
  ) {
    return 'revenue';
  }

  if (normalizedPath.startsWith('/profile')) {
    return 'retention';
  }

  return 'acquisition';
};

export const trackPageView = (path: string): void => {
  sendTelemetry({
    type: 'page_view',
    timestamp: new Date().toISOString(),
    path,
    metadata: {
      event_name: 'visit',
      event_stage: resolveStageByPath(path),
    },
  });
};

export const trackEvent = (
  name: TelemetryEventName,
  metadata: Record<string, unknown> = {},
  options: TrackEventOptions = {}
): void => {
  const eventMetadata: TelemetryMetadata = {
    event_name: name,
    ...metadata,
  };
  if (options.stage) {
    eventMetadata.event_stage = options.stage;
  }

  if (options.userState) {
    eventMetadata.user_state = options.userState;
  }

  if (options.attribution) {
    eventMetadata.attribution = options.attribution;
  }

  if (options.experimentId) {
    eventMetadata.experiment_id = options.experimentId;
  }

  if (options.variant) {
    eventMetadata.variant = options.variant;
  }

  sendTelemetry({
    type: 'event',
    timestamp: new Date().toISOString(),
    path: typeof window !== 'undefined' ? window.location.pathname : '/',
    message: name,
    metadata: eventMetadata,
  });
};

export const captureError = (error: Error, metadata: Record<string, unknown> = {}): void => {
  sendTelemetry({
    type: 'frontend_error',
    timestamp: new Date().toISOString(),
    path: window.location.pathname,
    message: error.message,
    stack: error.stack,
    metadata,
  });
};

export const initializeFrontendTelemetry = (): void => {
  if (isInitialized) {
    return;
  }

  isInitialized = true;
  captureAttributionFromUrl();

  if (!canSendTelemetry()) {
    return;
  }

  window.addEventListener('error', event => {
    if (event.error instanceof Error) {
      captureError(event.error, { source: 'window.error' });
      return;
    }

    sendTelemetry({
      type: 'frontend_error',
      timestamp: new Date().toISOString(),
      path: window.location.pathname,
      message: event.message || 'Unknown script error',
      metadata: { source: 'window.error' },
    });
  });

  window.addEventListener('unhandledrejection', event => {
    const reason = event.reason;
    if (reason instanceof Error) {
      sendTelemetry({
        type: 'unhandled_rejection',
        timestamp: new Date().toISOString(),
        path: window.location.pathname,
        message: reason.message,
        stack: reason.stack,
      });
      return;
    }

    sendTelemetry({
      type: 'unhandled_rejection',
      timestamp: new Date().toISOString(),
      path: window.location.pathname,
      message: typeof reason === 'string' ? reason : 'Unhandled promise rejection',
      metadata: { reason },
    });
  });

  sendTelemetry({
    type: 'app_start',
    timestamp: new Date().toISOString(),
    path: window.location.pathname,
    metadata: {
      userAgent: navigator.userAgent,
      language: navigator.language,
    },
  });
};
