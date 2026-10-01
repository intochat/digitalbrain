import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  tearDown(() => DigitalBrainHostEnv.servedConfig = const {});

  test('served config supplies the kernel url when no define is baked', () {
    DigitalBrainHostEnv.servedConfig = {
      DigitalBrainHostEnv.uiBaseVariable: 'http://localhost:5000',
      DigitalBrainHostEnv.shellVariable: 'desk-2',
      DigitalBrainHostEnv.chatVariable: 'side',
    };

    expect(
      DigitalBrainHostEnv.resolveUiBaseRaw(
        fromDefine: '',
        processEnvironment: const {},
      ),
      'http://localhost:5000',
    );
    expect(
      DigitalBrainHostEnv.resolveShell(
        fromDefine: '',
        processEnvironment: const {},
      ),
      'desk-2',
    );
    expect(
      DigitalBrainHostEnv.resolveChat(
        fromDefine: '',
        processEnvironment: const {},
      ),
      'side',
    );
  });

  test('a baked define beats the served config', () {
    DigitalBrainHostEnv.servedConfig = {
      DigitalBrainHostEnv.uiBaseVariable: 'http://served:1',
    };

    expect(
      DigitalBrainHostEnv.resolveUiBaseRaw(
        fromDefine: 'http://baked:2',
        processEnvironment: const {},
      ),
      'http://baked:2',
    );
  });

  test('absent served config falls back to the process environment', () {
    expect(
      DigitalBrainHostEnv.resolveUiBaseRaw(
        fromDefine: '',
        processEnvironment: {
          DigitalBrainHostEnv.uiBaseVariable: 'http://process:3',
        },
      ),
      'http://process:3',
    );
  });
}
