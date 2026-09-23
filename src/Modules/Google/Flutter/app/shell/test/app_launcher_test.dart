import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/app_launcher.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('the launcher falls back to the built-in first-party entries', () {
    final entries = launcherEntries(const []);
    expect(entries.map((e) => e.launchKey), ['files', 'images', 'mydata']);
    expect(entries.first.title, 'Files');
    expect(entries.first.subtitle, 'On this computer');
  });

  test('first-party manifests drive the launcher keys and labels', () {
    final entries = launcherEntries(const [
      AppManifestSummary(
        id: 'intochat.mydata',
        name: 'My Data',
        description: 'ignored for known apps',
        kind: 'declarative',
      ),
      AppManifestSummary(
        id: 'intochat.files',
        name: 'Files',
        description: 'ignored for known apps',
        kind: 'declarative',
        uiEntry: 'app-files',
      ),
      AppManifestSummary(
        id: 'intochat.image-editor',
        name: 'Image Editor',
        description: 'ignored for known apps',
        kind: 'declarative',
      ),
    ]);
    expect(entries.map((e) => e.launchKey), ['files', 'images', 'mydata']);
    expect(
      entries.singleWhere((e) => e.launchKey == 'files').subtitle,
      'On this computer',
    );
  });

  test('an app the shell cannot open yet is not offered', () {
    final entries = launcherEntries(const [
      AppManifestSummary(
        id: 'intochat.customer-tables',
        name: 'Customer Tables',
        description: 'Read live tables',
        kind: 'declarative',
      ),
    ]);
    expect(entries, isEmpty);
  });
}
