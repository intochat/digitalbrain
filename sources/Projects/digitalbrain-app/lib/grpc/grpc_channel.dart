import 'package:grpc/service_api.dart';
import 'package:grpc/grpc_or_grpcweb.dart';

import 'package:digitalbrain_flutter/telemetry/grpc_interceptor.dart';

dynamic createKernelChannel({
  required String host,
  required int port,
  required bool secure,
}) {
  return GrpcOrGrpcWebClientChannel.toSingleEndpoint(
    host: host,
    port: port,
    transportSecure: secure,
  );
}

// OTel-aware interceptors for kernel gRPC clients. Pass to generated client
// ctors via `interceptors: kernelInterceptors()` so traceparent + spans +
// per-call metrics flow through every RPC.
List<ClientInterceptor> kernelInterceptors() => [OtelGrpcInterceptor()];
