import 'package:opentelemetry/api.dart' as otel_api;
import 'package:opentelemetry/sdk.dart' as otel_sdk;

import 'otlp_log_exporter.dart';
import 'otlp_metric_exporter.dart';
import 'platform_env.dart';

class DigitalBrainTelemetry {
  static DigitalBrainTelemetry? _instance;

  final otel_sdk.TracerProviderBase tracerProvider;
  final otel_api.Tracer tracer;

  final OtlpLogExporter logExporter;
  final OtlpLogger logger;

  final OtlpMetricExporter metricExporter;

  late final SimpleCounter grpcRequests;
  late final SimpleHistogram grpcDuration;
  late final SimpleCounter chatMessages;
  late final SimpleCounter errors;

  DigitalBrainTelemetry._({
    required this.tracerProvider,
    required this.tracer,
    required this.logExporter,
    required this.logger,
    required this.metricExporter,
  }) {
    grpcRequests = metricExporter.createCounter(
      'digitalbrain.grpc.requests',
      unit: 'requests',
      description: 'gRPC calls from Flutter client',
    );
    grpcDuration = metricExporter.createHistogram(
      'digitalbrain.grpc.duration',
      unit: 'ms',
      description: 'gRPC call duration',
    );
    chatMessages = metricExporter.createCounter(
      'digitalbrain.chat.messages',
      unit: 'messages',
      description: 'Chat messages sent',
    );
    errors = metricExporter.createCounter(
      'digitalbrain.errors',
      unit: 'errors',
      description: 'Client-side errors',
    );
  }

  static DigitalBrainTelemetry get instance => _instance!;
  static bool get isInitialized => _instance != null;

  static void initialize({String? otlpEndpoint}) {
    if (_instance != null) return;

    final endpoint = otlpEndpoint ?? _resolveEndpoint();
    final headers = _resolveHeaders();

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
    final tracer = tracerProvider.getTracer('digitalbrain-flutter');

    final logExporter = OtlpLogExporter(
      endpoint: '$endpoint/v1/logs',
      serviceName: 'digitalbrain-flutter',
      headers: headers,
    );

    final metricExporter = OtlpMetricExporter(
      endpoint: '$endpoint/v1/metrics',
      serviceName: 'digitalbrain-flutter',
      headers: headers,
    );

    _instance = DigitalBrainTelemetry._(
      tracerProvider: tracerProvider,
      tracer: tracer,
      logExporter: logExporter,
      logger: OtlpLogger(logExporter),
      metricExporter: metricExporter,
    );
  }

  static String _resolveEndpoint() {
    final otlpEndpoint = getEnv('OTEL_EXPORTER_OTLP_ENDPOINT');
    final otlpProtocol = getEnv('OTEL_EXPORTER_OTLP_PROTOCOL')?.toLowerCase();
    if (otlpEndpoint != null &&
        otlpEndpoint.isNotEmpty &&
        otlpProtocol != 'grpc') {
      return otlpEndpoint.replaceAll(RegExp(r'/+$'), '');
    }
    const fallback = String.fromEnvironment(
      'OTLP_ENDPOINT',
      defaultValue: 'http://localhost:21017',
    );
    return fallback;
  }

  static Map<String, String> _resolveHeaders() {
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
