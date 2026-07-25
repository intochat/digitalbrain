const flutterWireNamespace = 'DigitalBrain.Flutter';

const flutterWireRecordNames = <String>{
  'ControlActivated',
  'OpenScene',
  'SceneOpened',
};

const flutterWireInterfaceNames = <String>{
  'IScene',
  'IShell',
};

const flutterWireAliases = <String, String>{
  'ControlActivated': 'flutter.control-activated',
  'IScene': 'DigitalBrain.Flutter.IScene',
  'IShell': 'DigitalBrain.Flutter.IShell',
  'OpenScene': 'flutter.open-scene',
  'SceneOpened': 'flutter.scene-opened',
};

const controlActivatedFields = <String>['ControlId', 'Intent', 'SceneKey'];
const openSceneFields = <String>['CommandId', 'SceneKey', 'Title'];
const sceneOpenedFields = <String>['CommandId', 'SceneKey', 'Shell', 'Title'];
