import { ZoneContextManager } from '@opentelemetry/context-zone';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { UserInteractionInstrumentation } from '@opentelemetry/instrumentation-user-interaction';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-base';
import { WebTracerProvider } from '@opentelemetry/sdk-trace-web';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';

interface OtelConfig {
  serviceName: string;
  endpoint: string;
  headers: string;
}

function parseOtelHeaders(raw: string): Record<string, string> {
  if (!raw) return {};
  return Object.fromEntries(
    raw
      .split(',')
      .map(pair => {
        const idx = pair.indexOf('=');
        if (idx < 1) return null;
        return [pair.slice(0, idx).trim(), pair.slice(idx + 1).trim()];
      })
      .filter((entry): entry is [string, string] => entry !== null)
  );
}

export function initializeOtel({ serviceName, endpoint, headers }: OtelConfig) {
  const resource = resourceFromAttributes({
    [ATTR_SERVICE_NAME]: serviceName,
  });

  const traceUrl = endpoint ? `${endpoint.replace(/\/$/, '')}/v1/traces` : '/v1/traces';

  const exporter = new OTLPTraceExporter({
    url: traceUrl,
    headers: parseOtelHeaders(headers),
  });

  const provider = new WebTracerProvider({
    resource,
    spanProcessors: [new BatchSpanProcessor(exporter)],
  });

  provider.register({
    contextManager: new ZoneContextManager(),
  });

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        propagateTraceHeaderCorsUrls: [/localhost/, /tripradar\.io/],
        clearTimingResources: true,
        ignoreUrls: [/\/v1\/traces/, /\/v1\/metrics/, /\/v1\/logs/],
      }),
      new UserInteractionInstrumentation({
        eventNames: ['click', 'submit'],
      }),
    ],
  });
}
