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

  test('external identity configuration normalizes required OIDC values', () {
    final configuration = parseExternalIdentityConfiguration(
      '  https://issuer.example/tenant  ',
      '  digitalbrain-ui  ',
      'profile,openid;email profile',
    );

    expect(configuration.issuer, Uri.parse('https://issuer.example/tenant'));
    expect(configuration.clientId, 'digitalbrain-ui');
    expect(
      configuration.scopes,
      unorderedEquals(['openid', 'profile', 'email']),
    );
  });

  test(
    'external identity configuration rejects unsafe or incomplete values',
    () {
      final invalidConfigurations = <List<String>>[
        ['', 'digitalbrain-ui', 'openid'],
        ['http://issuer.example/tenant', 'digitalbrain-ui', 'openid'],
        ['https://user@issuer.example/tenant', 'digitalbrain-ui', 'openid'],
        [
          'https://issuer.example/tenant?private=value',
          'digitalbrain-ui',
          'openid',
        ],
        ['https://issuer.example/tenant', '', 'openid'],
        ['https://issuer.example/tenant', 'digitalbrain\nui', 'openid'],
        ['https://issuer.example/tenant', 'digitalbrain-ui', 'profile email'],
        [
          'https://issuer.example/tenant',
          'digitalbrain-ui',
          'openid profile\u007f',
        ],
        [
          'https://issuer.example/tenant',
          'digitalbrain-ui',
          'openid ${List.generate(17, (index) => 'scope$index').join(' ')}',
        ],
      ];

      for (final values in invalidConfigurations) {
        expect(
          () => parseExternalIdentityConfiguration(
            values[0],
            values[1],
            values[2],
          ),
          throwsFormatException,
        );
      }
    },
  );
}
