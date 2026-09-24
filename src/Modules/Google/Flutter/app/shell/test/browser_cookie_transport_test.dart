import 'package:digitalbrain_flutter/src/host_environment.dart';
import 'package:digitalbrain_flutter/src/session_http_client_io.dart'
    if (dart.library.html) 'package:digitalbrain_flutter/src/session_http_client_web.dart'
    as transport;
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('local browser kernel keeps cookies same-site and preserves port', () {
    expect(
      browserKernelUri(
        Uri.parse('http://localhost:27879/api'),
        Uri.parse('http://127.0.0.1:27880/'),
      ),
      Uri.parse('http://127.0.0.1:27879/api'),
    );
    expect(
      browserKernelUri(
        Uri.parse('https://kernel.example/api'),
        Uri.parse('http://127.0.0.1:27880/'),
      ),
      Uri.parse('https://kernel.example/api'),
    );
    expect(
      browserKernelUri(Uri.parse('http://localhost:27879'), null),
      Uri.parse('http://localhost:27879'),
    );
  });
  test('browser transport explicitly sends session credentials', () {
    final client = transport.createSessionHttpClient();
    if (kIsWeb) expect((client as dynamic).withCredentials, isTrue);
    client.close();
  });
}
