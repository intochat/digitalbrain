import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test(
    'host env pins match FlutterHostingExtensions exclusive WithFlutterHost keys',
    () {
      expect(DigitalBrainHostEnv.uiBaseVariable, 'DIGITALBRAIN_UI_BASE');
      expect(DigitalBrainHostEnv.shellVariable, 'DIGITALBRAIN_SHELL');
      expect(DigitalBrainHostEnv.chatVariable, 'DIGITALBRAIN_CHAT');
      expect(DigitalBrainHostEnv.defaultShellName, 'desk');
      expect(DigitalBrainHostEnv.defaultChatName, 'main');
      expect(
        DigitalBrainHostEnv.hostProcessVariables,
        unorderedEquals(const {
          'DIGITALBRAIN_UI_BASE',
          'DIGITALBRAIN_SHELL',
          'DIGITALBRAIN_CHAT',
        }),
      );
      expect(DigitalBrainHostEnv.hostProcessVariables, hasLength(3));
    },
  );

  test('resolveUiBaseRaw prefers compile-time define over process env', () {
    final raw = DigitalBrainHostEnv.resolveUiBaseRaw(
      fromDefine: 'http://define.example:9',
      processEnvironment: {
        DigitalBrainHostEnv.uiBaseVariable: 'http://process.example:8',
      },
    );
    expect(raw, 'http://define.example:9');
  });

  test('resolveUiBaseRaw falls back to process env when define empty', () {
    final raw = DigitalBrainHostEnv.resolveUiBaseRaw(
      fromDefine: '',
      processEnvironment: {
        DigitalBrainHostEnv.uiBaseVariable: 'http://localhost:5100',
      },
    );
    expect(raw, 'http://localhost:5100');
  });

  test(
    'requireUiBaseUri parses process env and rejects missing base on VM',
    () {
      final uri = DigitalBrainHostEnv.requireUiBaseUri(
        fromDefine: '',
        processEnvironment: {
          DigitalBrainHostEnv.uiBaseVariable: 'http://localhost:5100/',
        },
      );
      expect(uri.scheme, 'http');
      expect(uri.host, 'localhost');
      expect(uri.port, 5100);

      // Pure-Dart VM host has no browser same-origin fallback.
      expect(
        () => DigitalBrainHostEnv.requireUiBaseUri(
          fromDefine: '',
          processEnvironment: const {},
        ),
        throwsA(
          isA<StateError>().having(
            (e) => e.message,
            'message',
            contains(DigitalBrainHostEnv.uiBaseVariable),
          ),
        ),
      );
    },
  );

  test('resolveShell prefers define, then process, then default desk', () {
    expect(
      DigitalBrainHostEnv.resolveShell(
        fromDefine: 'ops',
        processEnvironment: {
          DigitalBrainHostEnv.shellVariable: 'process-shell',
        },
      ),
      'ops',
    );
    expect(
      DigitalBrainHostEnv.resolveShell(
        fromDefine: '',
        processEnvironment: {
          DigitalBrainHostEnv.shellVariable: 'process-shell',
        },
      ),
      'process-shell',
    );
    expect(
      DigitalBrainHostEnv.resolveShell(
        fromDefine: '',
        processEnvironment: const {},
      ),
      DigitalBrainHostEnv.defaultShellName,
    );
  });

  test('DigitalBrainUiClient.fromEnvironment reads injected process map', () {
    final client = DigitalBrainUiClient.fromEnvironment(
      processEnvironment: {
        DigitalBrainHostEnv.uiBaseVariable: 'http://edge.test:7',
      },
    );
    expect(client.baseUri.toString(), 'http://edge.test:7');
    client.close();
  });
}
