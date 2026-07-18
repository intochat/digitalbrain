import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:opentelemetry/api.dart' as otel_api;
import 'package:opentelemetry/sdk.dart' as otel_sdk;

import 'otlp_log_exporter.dart';
import 'otlp_metric_exporter.dart';
import 'platform_env.dart';

class InoTelemetry {
  static InoTelemetry? _instance;

  final otel_sdk.TracerProviderBase tracerProvider;
  final otel_api.Tracer tracer;

  final OtlpLogExporter logExporter;
  final OtlpLogger logger;

  final OtlpMetricExporter metricExporter;

  // pre-registered metrics
  late final SimpleCounter grpcRequests;
  late final SimpleHistogram grpcDuration;
  late final SimpleCounter chatMessages;
  late final SimpleCounter errors;

  InoTelemetry._({
    required this.tracerProvider,
    required this.tracer,
    required this.logExporter,
    required this.logger,
    required this.metricExporter,
  }) {
    grpcRequests = metricExporter.createCounter(
      'ino.grpc.requests',
      unit: 'requests',
      description: 'gRPC calls from Flutter client',
    );
    grpcDuration = metricExporter.createHistogram(
      'ino.grpc.duration',
      unit: 'ms',
      description: 'gRPC call duration',
    );
    chatMessages = metricExporter.createCounter(
      'ino.chat.messages',
      unit: 'messages',
      description: 'Chat messages sent',
    );
    errors = metricExporter.createCounter(
      'ino.errors',
      unit: 'errors',
      description: 'Client-side errors',
    );
  }

  static InoTelemetry get instance => _instance!;
  static bool get isInitialized => _instance != null;

  static void initialize({String? otlpEndpoint}) {
    if (_instance != null) return;

    final endpoint = otlpEndpoint ?? _resolveEndpoint();

    final headers = _resolveHeaders();

    // traces via opentelemetry SDK — protobuf OTLP export
    final tracerProvider = otel_sdk.TracerProviderBase(
      processors: [
        otel_sdk.BatchSpanProcessor(
          otel_sdk.CollectorExporter(
            Uri.parse('$endpoint/v1/traces'),
            headers: headers,
          ),
        ),
      ],
    );
    otel_api.registerGlobalTracerProvider(tracerProvider);
    final tracer = tracerProvider.getTracer('ino-flutter');

    // logs via custom OTLP/JSON exporter
    final logExporter = OtlpLogExporter(
      endpoint: '$endpoint/v1/logs',
      serviceName: 'ino-flutter',
      headers: headers,
    );

    // metrics via custom OTLP/JSON exporter
    final metricExporter = OtlpMetricExporter(
      endpoint: '$endpoint/v1/metrics',
      serviceName: 'ino-flutter',
      headers: headers,
    );

    _instance = InoTelemetry._(
      tracerProvider: tracerProvider,
      tracer: tracer,
      logExporter: logExporter,
      logger: OtlpLogger(logExporter),
      metricExporter: metricExporter,
    );
  }

  static String _resolveEndpoint() {
    if (kIsWeb) {
      // web: bridge through Telegram service (same origin, no CORS)
      return '${Uri.base.origin}/otlp';
    }
    // native: Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT pointing
    // directly to the dashboard's OTLP HTTP endpoint — no proxy needed
    final otlpEndpoint = getEnv('OTEL_EXPORTER_OTLP_ENDPOINT');
    if (otlpEndpoint != null && otlpEndpoint.isNotEmpty) {
      return otlpEndpoint.replaceAll(RegExp(r'/+$'), '');
    }
    const fallback = String.fromEnvironment(
      'OTLP_ENDPOINT',
      defaultValue: 'http://localhost:21017',
    );
    return fallback;
  }

  /// Auth headers from Aspire's OTEL_EXPORTER_OTLP_HEADERS (native only).
  static Map<String, String> _resolveHeaders() {
    if (kIsWeb) return {};
    final raw = getEnv('OTEL_EXPORTER_OTLP_HEADERS');
    if (raw == null || raw.isEmpty) return {};
    final headers = <String, String>{};
    for (final pair in raw.split(',')) {
      final idx = pair.indexOf('=');
      if (idx > 0) {
        headers[pair.substring(0, idx).trim()] = pair.substring(idx + 1).trim();
      }
    }
    return headers;
  }

  static Future<void> shutdown() async {
    final t = _instance;
    if (t == null) return;
    await t.logExporter.shutdown();
    await t.metricExporter.shutdown();
    _instance = null;
  }
}
