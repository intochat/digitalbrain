import 'package:digitalbrain_flutter/v2/v2_config.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('V2 endpoint accepts an absolute HTTPS origin', () {
    expect(
      parseV2UiEndpoint('https://localhost:7443/'),
      Uri.parse('https://localhost:7443'),
    );
  });

  test(
    'V2 endpoint rejects plaintext, paths, metadata, and non-HTTPS schemes',
    () {
      for (final source in [
        'http://127.0.0.1:5080',
        'https://localhost:7443/v2',
        'https://localhost:7443?workspace=private',
        'https://user:pass@localhost:7443',
        'file:///tmp/socket',
        'localhost:7443',
      ]) {
        expect(() => parseV2UiEndpoint(source), throwsFormatException);
      }
    },
  );
}
