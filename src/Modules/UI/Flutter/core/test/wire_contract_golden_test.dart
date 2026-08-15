import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

void main() {
  test('flutter wire contracts golden matches C# contracts', () {
    final candidates = [
      Directory.current.uri.resolve('../../../Contracts/flutter-wire-contracts.golden.json'),
      Directory.current.uri.resolve('../../../../Contracts/flutter-wire-contracts.golden.json'),
    ];
    File? golden;
    for (final uri in candidates) {
      final file = File.fromUri(uri);
      if (file.existsSync()) {
        golden = file;
        break;
      }
    }
    expect(golden, isNotNull, reason: 'flutter-wire-contracts.golden.json not found');
    final manifest = jsonDecode(golden!.readAsStringSync()) as Map<String, Object?>;
    expect(manifest['namespace'], 'DigitalBrain.UI');
    expect(manifest['version'], 1);
    final types = (manifest['types'] as List).cast<Map>();
    final aliases = types.map((t) => t['alias']).toSet();
    expect(aliases, containsAll(['ui.surface', 'ui.open-surface', 'ui.surface-opened', 'ui.button', 'ui.button-clicked']));
  });
}
