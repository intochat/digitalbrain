import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('resolveEndpointFrom', () {
    test('uses explicit kernel endpoint before the web app host', () {
      final endpoint = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse(
          'https://gentle-sand-0f4081803.7.azurestaticapps.net/#/chat',
        ),
        kernelEndpoint:
            'https://digitalbrain-jobs.agreeablefield-fcde995f.westeurope.azurecontainerapps.io',
      );

      expect(endpoint, (
        'digitalbrain-jobs.agreeablefield-fcde995f.westeurope.azurecontainerapps.io',
        443,
        true,
      ));
    });

    test('falls back to the web app host only without explicit endpoint', () {
      final endpoint = resolveEndpointFrom(
        isWeb: true,
        base: Uri.parse(
          'https://gentle-sand-0f4081803.7.azurestaticapps.net/#/chat',
        ),
      );

      expect(endpoint, (
        'gentle-sand-0f4081803.7.azurestaticapps.net',
        443,
        true,
      ));
    });
  });
}
