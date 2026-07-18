import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import 'app/i18n';
import { env } from 'shared/config/env';
import { initializeFrontendTelemetry, initializeOtel } from 'shared/lib';
import { App } from './App.tsx';
import './index.css';

if (env.OTEL_ENABLED) {
  initializeOtel({
    serviceName: env.OTEL_SERVICE_NAME,
    endpoint: env.OTEL_ENDPOINT,
    headers: env.OTEL_HEADERS,
  });
}

initializeFrontendTelemetry();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
