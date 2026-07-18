import { execFileSync } from 'child_process';
import fs from 'fs';
import os from 'os';
import path from 'path';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';

const DEV_CERT_DIR = path.join(os.tmpdir(), 'tripradar-vite-dev-cert');
const DEV_CERT_PATH = path.join(DEV_CERT_DIR, 'localhost.pfx');
const DEV_CERT_PASSWORD = 'tripradar-local-dev-cert';

function getHttpsOptions(env: Record<string, string>) {
  const httpsEnabled = (env.VITE_DEV_HTTPS || 'true').toLowerCase() !== 'false';
  if (!httpsEnabled) {
    return undefined;
  }

  const certPath = env.VITE_DEV_HTTPS_CERT_PATH || DEV_CERT_PATH;
  const certPassword = env.VITE_DEV_HTTPS_CERT_PASSWORD || DEV_CERT_PASSWORD;

  if (!fs.existsSync(certPath)) {
    fs.mkdirSync(path.dirname(certPath), { recursive: true });
    execFileSync('dotnet', ['dev-certs', 'https', '--export-path', certPath, '--password', certPassword]);
  }

  return {
    pfx: fs.readFileSync(certPath),
    passphrase: certPassword,
  };
}

// https://vitejs.dev/config/
const config = defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, '');
  const apiProxyTarget = env.VITE_API_BASE_URL || 'http://localhost:5330';
  const otelHttpEndpoint = process.env.ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL || 'https://localhost:4318';
  const otelProxyHeaders: Record<string, string> = {};
  (process.env.OTEL_EXPORTER_OTLP_HEADERS || '').split(',').forEach(pair => {
    const idx = pair.indexOf('=');
    if (idx > 0) otelProxyHeaders[pair.slice(0, idx).trim()] = pair.slice(idx + 1).trim();
  });
  const devServerHost = env.VITE_DEV_HOST || 'localhost';
  const devServerPort = Number.parseInt(env.VITE_DEV_PORT || '3000', 10);
  const configuredHosts = (env.VITE_ALLOWED_HOSTS || '')
    .split(',')
    .map(host => host.trim())
    .filter(Boolean);
  const allowedHosts = [...new Set(['.localhost', '.tripradar.io', '.trycloudflare.com', ...configuredHosts])];
  const https = getHttpsOptions(env);
  return {
    base: '/',
    envDir: __dirname,
    plugins: [react()],
    optimizeDeps: {
      exclude: ['lucide-react'],
    },
    server: {
      host: devServerHost,
      port: Number.isNaN(devServerPort) ? 3000 : devServerPort,
      strictPort: true,
      https,
      allowedHosts,
      proxy: {
        '/api': {
          target: apiProxyTarget,
          changeOrigin: true,
          secure: false,
        },
        '/v1/traces': {
          target: otelHttpEndpoint,
          changeOrigin: true,
          secure: false,
          headers: otelProxyHeaders,
        },
        '/v1/metrics': {
          target: otelHttpEndpoint,
          changeOrigin: true,
          secure: false,
          headers: otelProxyHeaders,
        },
        '/v1/logs': {
          target: otelHttpEndpoint,
          changeOrigin: true,
          secure: false,
          headers: otelProxyHeaders,
        },
      },
    },
    resolve: {
      alias: {
        app: path.resolve(__dirname, './src/app'),
        pages: path.resolve(__dirname, './src/pages'),
        widgets: path.resolve(__dirname, './src/widgets'),
        features: path.resolve(__dirname, './src/features'),
        entities: path.resolve(__dirname, './src/entities'),
        shared: path.resolve(__dirname, './src/shared'),
      },
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: './src/test/setup.ts',
    },
  };
});

export default config;
