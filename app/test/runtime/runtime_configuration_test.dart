import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('runtime endpoint accepts an absolute HTTPS origin', () {
    expect(
      parseUiEndpoint('https://localhost:7443/'),
      Uri.parse('https://localhost:7443'),
    );
  });

  test(
    'runtime endpoint rejects plaintext, paths, metadata, and non-HTTPS schemes',
    () {
      for (final source in [
        'http://127.0.0.1:5080',
        'https://localhost:7443/v2',
        'https://localhost:7443?workspace=private',
        'https://user:pass@localhost:7443',
        'file:///tmp/socket',
        'localhost:7443',
      ]) {
        expect(() => parseUiEndpoint(source), throwsFormatException);
      }
    },
  );

  test('Salesforce callback resolves to a trusted OAuth start origin', () {
    expect(
      parseSalesforceOAuthStartOrigin(
        'http://localhost:51014/oauth/callback/salesforce',
      ),
      Uri.parse('http://localhost:51014'),
    );
    expect(
      parseSalesforceOAuthStartOrigin(
        'https://brain.example/oauth/callback/salesforce',
      ),
      Uri.parse('https://brain.example'),
    );
  });

  test('Salesforce callback rejects untrusted or malformed origins', () {
    for (final source in [
      'http://brain.example/oauth/callback/salesforce',
      'https://brain.example/oauth/start/salesforce',
      'https://brain.example/oauth/callback/salesforce?state=unsafe',
      'https://user@brain.example/oauth/callback/salesforce',
    ]) {
      expect(
        () => parseSalesforceOAuthStartOrigin(source),
        throwsFormatException,
      );
    }
  });
}
