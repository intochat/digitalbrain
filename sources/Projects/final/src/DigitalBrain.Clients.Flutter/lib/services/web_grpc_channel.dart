import 'package:grpc/grpc_web.dart' as grpc_web;

/// Web-only implementation for creating the gRPC-Web channel.
/// This file is only imported on web (dart.library.js) via conditional import,
/// so its dependency on package:web (and js_interop) is never pulled into
/// Windows/desktop builds.
grpc_web.GrpcWebClientChannel createWebChannel(String host, int port) {
  return grpc_web.GrpcWebClientChannel.xhr(Uri.parse('http://$host:$port'));
}