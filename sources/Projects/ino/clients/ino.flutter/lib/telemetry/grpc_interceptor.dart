import 'package:grpc/grpc.dart';
import 'package:opentelemetry/api.dart' as otel;
import 'telemetry.dart';

/// Wraps every gRPC call in an OTel span and injects W3C traceparent so
/// backend spans become children of the Flutter-initiated trace.
class OtelGrpcInterceptor extends ClientInterceptor {
  @override
  ResponseFuture<R> interceptUnary<Q, R>(
    ClientMethod<Q, R> method,
    Q request,
    CallOptions options,
    ClientUnaryInvoker<Q, R> invoker,
  ) {
    if (!InoTelemetry.isInitialized) {
      return invoker(method, request, options);
    }

    final tel = InoTelemetry.instance;
    final rpcMethod = method.path.split('/').last;
    final span = tel.tracer.startSpan(
      'grpc $rpcMethod',
      kind: otel.SpanKind.client,
    )
      ..setAttribute(otel.Attribute.fromString('rpc.system', 'grpc'))
      ..setAttribute(otel.Attribute.fromString('rpc.service', 'ino.Ino'))
      ..setAttribute(otel.Attribute.fromString('rpc.method', rpcMethod));

    final traceparent = _buildTraceparent(span.spanContext);
    final enrichedOptions = options.mergedWith(
      CallOptions(metadata: {'traceparent': traceparent}),
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
        tel.errors.add(1, attributes: {
          'error.type': 'grpc',
          'rpc.method': rpcMethod,
        });
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
    if (!InoTelemetry.isInitialized) {
      return invoker(method, requests, options);
    }

    final tel = InoTelemetry.instance;
    final rpcMethod = method.path.split('/').last;
    final span = tel.tracer.startSpan(
      'grpc $rpcMethod',
      kind: otel.SpanKind.client,
    )
      ..setAttribute(otel.Attribute.fromString('rpc.system', 'grpc'))
      ..setAttribute(otel.Attribute.fromString('rpc.service', 'ino.Ino'))
      ..setAttribute(otel.Attribute.fromString('rpc.method', rpcMethod));

    tel.grpcRequests.add(1, attributes: {
      'rpc.method': rpcMethod,
      'rpc.type': 'streaming',
    });

    final traceparent = _buildTraceparent(span.spanContext);
    final enrichedOptions = options.mergedWith(
      CallOptions(metadata: {'traceparent': traceparent}),
    );

    // grpc-dart ResponseStream is single-subscription: listening here for
    // telemetry and then handing the stream back to the caller fails with
    // "Stream has already been listened to" when the caller subscribes via
    // await-for. Close the span eagerly on grpc.onDone via the ResponseStream
    // trailers future (which does NOT consume the response stream) — proper
    // duration / error telemetry is punted to a StreamTransformer follow-up.
    final response = invoker(method, requests, enrichedOptions);

    response.trailers.then(
      (_) => span
        ..setStatus(otel.StatusCode.ok)
        ..end(),
      onError: (Object error) {
        span
          ..setStatus(otel.StatusCode.error, error.toString())
          ..end();
        tel.errors.add(1, attributes: {
          'error.type': 'grpc_stream',
          'rpc.method': rpcMethod,
        });
      },
    );

    return response;
  }

  void _recordMetrics(
    InoTelemetry tel,
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

  /// W3C Trace Context: 00-{traceId 32hex}-{spanId 16hex}-{flags 2hex}
  String _buildTraceparent(otel.SpanContext ctx) {
    return '00-${ctx.traceId}-${ctx.spanId}-01';
  }
}
