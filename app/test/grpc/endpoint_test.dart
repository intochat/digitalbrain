import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';

void main() {
  group('resolveEndpointFrom', () {
    test('web honors a configured absolute KERNEL_ENDPOINT host', () {
      final (host, port, secure) = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse('https://digitalbrain.tech/'),
        kernelEndpoint: 'https://api.digitalbrain.tech',
        aspireKernelUrl: null,
      );
      expect(host, 'api.digitalbrain.tech');
      expect(secure, isTrue);
    });

    test('web with no config falls back to same-origin host', () {
      final (host, _, secure) = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse('https://digitalbrain.tech/'),
        kernelEndpoint: '',
        aspireKernelUrl: null,
      );
      expect(host, 'digitalbrain.tech');
      expect(secure, isTrue);
    });

    test('non-web honors a configured KERNEL_ENDPOINT', () {
      final (host, port, secure) = resolveEndpointFrom(
        isWeb: false,
        base: Uri.parse('http://localhost/'),
        kernelEndpoint: 'https://localhost:59066',
        aspireKernelUrl: null,
      );
      expect(host, 'localhost');
      expect(port, 59066);
      expect(secure, isTrue);
    });

    test('desktop Aspire endpoint selection prefers grpc over web', () {
      final url = resolveAspireKernelUrl(
        grpcUrl: 'http://localhost:60419',
        webUrl: 'http://localhost:60420',
      );

      expect(url, 'http://localhost:60419');
    });
  });
}
