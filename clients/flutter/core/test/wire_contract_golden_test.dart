import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

const _expectedWireManifest = <String, Object?>{
  'version': 1,
  'namespace': 'DigitalBrain.Shell',
  'types': [
    {
      'name': 'ControlActivated',
      'kind': 'record',
      'alias': 'flutter.control-activated',
      'properties': [
        {'name': 'ControlId', 'type': 'String'},
        {'name': 'Intent', 'type': 'String'},
        {'name': 'SceneKey', 'type': 'String'},
      ],
      'methods': <Object?>[],
    },
    {
      'name': 'IScene',
      'kind': 'interface',
      'alias': 'flutter.scene',
      'properties': <Object?>[],
      'methods': <Object?>[],
    },
    {
      'name': 'IShell',
      'kind': 'interface',
      'alias': 'flutter.shell',
      'properties': <Object?>[],
      'methods': [
        {
          'name': 'Open',
          'alias': 'Open',
          'parameters': [
            {'name': 'command', 'type': 'OpenScene'},
          ],
          'returnType': 'Task',
        },
      ],
    },
    {
      'name': 'OpenScene',
      'kind': 'record',
      'alias': 'flutter.open-scene',
      'properties': [
        {'name': 'CommandId', 'type': 'CommandId'},
        {'name': 'SceneKey', 'type': 'String'},
        {'name': 'Title', 'type': 'String'},
      ],
      'methods': <Object?>[],
    },
    {
      'name': 'SceneOpened',
      'kind': 'record',
      'alias': 'flutter.scene-opened',
      'properties': [
        {'name': 'CommandId', 'type': 'CommandId'},
        {'name': 'SceneKey', 'type': 'String'},
        {'name': 'Shell', 'type': 'NeuronId'},
        {'name': 'Title', 'type': 'String'},
      ],
      'methods': <Object?>[],
    },
  ],
};

void main() {
  test(
    'Dart wire pin deep-equals Shell.Contracts golden (dual golden equality)',
    () {
      final goldenFile = _locateGolden();
      final actual = jsonDecode(goldenFile.readAsStringSync());
      expect(actual, _expectedWireManifest);
    },
  );
}

File _locateGolden() {
  var dir = Directory.current;
  for (var i = 0; i < 8; i++) {
    final candidate = File(
      '${dir.path}/src/modules/shell/DigitalBrain.Modules.Shell.Contracts/flutter-wire-contracts.golden.json',
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
