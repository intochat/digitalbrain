import 'dart:convert';
import 'dart:io';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';
import 'package:test/test.dart';

void main() {
  test('Dart wire pins match Flutter.Contracts golden manifest', () {
    final goldenFile = _locateGolden();
    final root = jsonDecode(goldenFile.readAsStringSync()) as Map<String, dynamic>;

    expect(root['version'], 1);
    expect(root['namespace'], flutterWireNamespace);

    final types = (root['types'] as List<dynamic>).cast<Map<String, dynamic>>();
    final byName = {
      for (final type in types) type['name'] as String: type,
    };

    expect(byName.keys.toSet(), {
      ...flutterWireRecordNames,
      ...flutterWireInterfaceNames,
    });

    for (final entry in flutterWireAliases.entries) {
      expect(byName[entry.key]?['alias'], entry.value);
    }

    expect(_propertyNames(byName['ControlActivated']!), controlActivatedFields);
    expect(_propertyNames(byName['OpenScene']!), openSceneFields);
    expect(_propertyNames(byName['SceneOpened']!), sceneOpenedFields);

    final shell = byName['IShell']!;
    final methods = (shell['methods'] as List<dynamic>).cast<Map<String, dynamic>>();
    expect(methods.single['name'], 'Open');
    expect(methods.single['alias'], 'Open');
  });
}

List<String> _propertyNames(Map<String, dynamic> type) {
  final properties = (type['properties'] as List<dynamic>).cast<Map<String, dynamic>>();
  return [for (final property in properties) property['name'] as String];
}

File _locateGolden() {
  var dir = Directory.current;
  for (var i = 0; i < 8; i++) {
    final candidate = File(
      '${dir.path}/modules/DigitalBrain.Modules.Flutter.Contracts/flutter-wire-contracts.golden.json',
    );
    if (candidate.existsSync()) {
      return candidate;
    }
    dir = dir.parent;
  }

  fail(
    'Could not locate flutter-wire-contracts.golden.json from ${Directory.current.path}',
  );
}
