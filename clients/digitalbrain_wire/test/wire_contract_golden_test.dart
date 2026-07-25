import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

// Dart pin of modules/.../flutter-wire-contracts.golden.json (single oracle file; no forked copy).
const _namespace = 'DigitalBrain.Flutter';
const _recordNames = <String>{
  'ControlActivated',
  'OpenScene',
  'SceneOpened',
};
const _interfaceNames = <String>{
  'IScene',
  'IShell',
};
const _aliases = <String, String>{
  'ControlActivated': 'flutter.control-activated',
  'IScene': 'DigitalBrain.Flutter.IScene',
  'IShell': 'DigitalBrain.Flutter.IShell',
  'OpenScene': 'flutter.open-scene',
  'SceneOpened': 'flutter.scene-opened',
};
const _controlActivatedFields = <String>['ControlId', 'Intent', 'SceneKey'];
const _openSceneFields = <String>['CommandId', 'SceneKey', 'Title'];
const _sceneOpenedFields = <String>['CommandId', 'SceneKey', 'Shell', 'Title'];

void main() {
  test('Dart wire pins match Flutter.Contracts golden manifest', () {
    final goldenFile = _locateGolden();
    final root = jsonDecode(goldenFile.readAsStringSync()) as Map<String, dynamic>;

    expect(root['version'], 1);
    expect(root['namespace'], _namespace);

    final types = (root['types'] as List<dynamic>).cast<Map<String, dynamic>>();
    final byName = {
      for (final type in types) type['name'] as String: type,
    };

    expect(byName.keys.toSet(), {
      ..._recordNames,
      ..._interfaceNames,
    });

    for (final entry in _aliases.entries) {
      expect(byName[entry.key]?['alias'], entry.value);
    }

    expect(_propertyNames(byName['ControlActivated']!), _controlActivatedFields);
    expect(_propertyNames(byName['OpenScene']!), _openSceneFields);
    expect(_propertyNames(byName['SceneOpened']!), _sceneOpenedFields);

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
