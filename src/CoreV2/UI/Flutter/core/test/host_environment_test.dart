import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:test/test.dart';

void main() {
  test('ProductHost base prefers dart define then falls back to process env', () {
    expect(
      DigitalBrainHostEnvironment.requireProductBase(
        fromDefine: 'http://define.example:5000',
        processEnvironment: const {
          DigitalBrainHostEnvironment.productBaseVariable:
              'http://process.example:5001',
        },
      ).port,
      5000,
    );
    expect(
      DigitalBrainHostEnvironment.requireProductBase(
        fromDefine: '',
        processEnvironment: const {
          DigitalBrainHostEnvironment.productBaseVariable:
              'http://process.example:5001',
        },
      ).port,
      5001,
    );
  });

  test('ProductHost base is mandatory and absolute', () {
    expect(
      () => DigitalBrainHostEnvironment.requireProductBase(
        fromDefine: '',
        processEnvironment: const {},
      ),
      throwsStateError,
    );
    expect(
      () => DigitalBrainHostEnvironment.requireProductBase(
        fromDefine: 'relative/path',
      ),
      throwsFormatException,
    );
  });
}
