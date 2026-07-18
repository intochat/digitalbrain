import 'package:grpc/service_api.dart';
import 'package:opentelemetry/api.dart' as otel;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'telemetry.dart';

class OtelGrpcInterceptor extends ClientInterceptor {
  @override
  ResponseFuture<R> interceptUnary<Q, R>(
    ClientMethod<Q, R> method,
    Q request,
    CallOptions options,
    ClientUnaryInvoker<Q, R> invoker,
  ) {
    final enrichedMetadata = Map<String, String>.from(options.metadata);

    if (!enrichedMetadata.containsKey('x-brain-id')) {
      bool isBootstrap = false;
      if (request is gw.SynapseEnvelope) {
        final type = request.typeName;
        isBootstrap = type == 'DigitalBrain.SDK.Identity.Contracts.RequestLogin' ||
            type == 'DigitalBrain.SDK.Identity.Contracts.RequestLoginCard' ||
            type == 'DigitalBrain.SDK.Identity.Contracts.RequestCreateBrain' ||
            type == 'DigitalBrain.Domains.Onboarding.Contracts.RequestOnboarding' ||
            type == 'DigitalBrain.Domains.Onboarding.Contracts.AcceptPolicy' ||
            type == 'DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted';
      }
      if (!isBootstrap) {
        enrichedMetadata['x-brain-id'] = 'primary';
        enrichedMetadata['x-active-scope'] = 'primary';
      }
    }

    if (!DigitalBrainTelemetry.isInitialized) {
      final enrichedOptions = options.mergedWith(
        CallOptions(metadata: enrichedMetadata),
      );
      return invoker(method, request, enrichedOptions);
    }

    final tel = DigitalBrainTelemetry.instance;
    final rpcMethod = method.path.split('/').last;
    final rpcService = method.path
        .split('/')
        .firstWhere((s) => s.isNotEmpty, orElse: () => 'unknown');
    final span =
        tel.tracer.startSpan('grpc $rpcMethod', kind: otel.SpanKind.client)
          ..setAttribute(otel.Attribute.fromString('rpc.system', 'grpc'))
          ..setAttribute(otel.Attribute.fromString('rpc.service', rpcService))
          ..setAttribute(otel.Attribute.fromString('rpc.method', rpcMethod));

    final traceparent = _buildTraceparent(span.spanContext);
    enrichedMetadata['traceparent'] = traceparent;

    final enrichedOptions = options.mergedWith(
      CallOptions(metadata: enrichedMetadata),
    );

    final stopwatch = Stopwatch()..start();
    final response = invoker(method, request, enrichedOptions);

    response.then(
      (_) {
        stopwatch.stop();
        span
          ..setStatus(otel.StatusCode.ok)
          ..end();
        _recordMetrics(tel, rpcMethod, stopwatch.elapsedMilliseconds, true);
      },
      onError: (Object error) {
        stopwatch.stop();
        span
          ..setStatus(otel.StatusCode.error, error.toString())
          ..end();
        _recordMetrics(tel, rpcMethod, stopwatch.elapsedMilliseconds, false);
        tel.errors.add(
          1,
          attributes: {'error.type': 'grpc', 'rpc.method': rpcMethod},
        );
      },
    );

    return response;
  }

  @override
  ResponseStream<R> interceptStreaming<Q, R>(
    ClientMethod<Q, R> method,
    Stream<Q> requests,
    CallOptions options,
    ClientStreamingInvoker<Q, R> invoker,
  ) {
    final enrichedMetadata = Map<String, String>.from(options.metadata);

    if (!enrichedMetadata.containsKey('x-brain-id')) {
      enrichedMetadata['x-brain-id'] = 'primary';
      enrichedMetadata['x-active-scope'] = 'primary';
    }

    if (!DigitalBrainTelemetry.isInitialized) {
      final enrichedOptions = options.mergedWith(
        CallOptions(metadata: enrichedMetadata),
      );
      return invoker(method, requests, enrichedOptions);
    }

    final tel = DigitalBrainTelemetry.instance;
    final rpcMethod = method.path.split('/').last;
    final rpcService = method.path
        .split('/')
        .firstWhere((s) => s.isNotEmpty, orElse: () => 'unknown');
    final span =
        tel.tracer.startSpan('grpc $rpcMethod', kind: otel.SpanKind.client)
          ..setAttribute(otel.Attribute.fromString('rpc.system', 'grpc'))
          ..setAttribute(otel.Attribute.fromString('rpc.service', rpcService))
          ..setAttribute(otel.Attribute.fromString('rpc.method', rpcMethod));

    tel.grpcRequests.add(
      1,
      attributes: {'rpc.method': rpcMethod, 'rpc.type': 'streaming'},
    );

    final traceparent = _buildTraceparent(span.spanContext);
    enrichedMetadata['traceparent'] = traceparent;

    final enrichedOptions = options.mergedWith(
      CallOptions(metadata: enrichedMetadata),
    );

    final response = invoker(method, requests, enrichedOptions);

    response.trailers.then(
      (_) => span
        ..setStatus(otel.StatusCode.ok)
        ..end(),
      onError: (Object error) {
        span
          ..setStatus(otel.StatusCode.error, error.toString())
          ..end();
        tel.errors.add(
          1,
          attributes: {'error.type': 'grpc_stream', 'rpc.method': rpcMethod},
        );
      },
    );

    return response;
  }

  void _recordMetrics(
    DigitalBrainTelemetry tel,
    String method,
    int durationMs,
    bool success,
  ) {
    final attrs = {
      'rpc.method': method,
      'rpc.status': success ? 'ok' : 'error',
    };
    tel.grpcRequests.add(1, attributes: attrs);
    tel.grpcDuration.record(durationMs.toDouble(), attributes: attrs);
  }

  String _buildTraceparent(otel.SpanContext ctx) {
    return '00-${ctx.traceId}-${ctx.spanId}-01';
  }
}
