/// Stub for non-web platforms (Windows, Linux, macOS, mobile, tests on VM).
/// The real implementation lives in web_grpc_channel.dart which is only
/// compiled for web via the conditional import in surface_stream_connection.dart.
/// This prevents 'package:web' + dart:js_interop from ever being part of the
/// Windows/desktop build graph.
dynamic createWebChannel(String host, int port) {
  throw UnsupportedError(
    'createWebChannel is only supported when running on web. '
    'On desktop use the regular grpc ClientChannel.',
  );
}