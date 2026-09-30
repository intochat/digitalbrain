import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/app_launcher.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('the shell surfaces are always offered', () {
    final entries = launcherEntries(const []);
    expect(entries.map((e) => e.launchKey), ['files', 'images']);
    expect(entries.first.title, 'Files');
    expect(entries.first.subtitle, 'Workspace assets');
  });

  test('a declarative catalog app is offered by its manifest id', () {
    final entries = launcherEntries(const [
      AppManifestSummary(
        id: 'intochat.saved-lead-form',
        name: 'Lead form',
        description: 'Enter a lead',
        kind: 'declarative',
        uiEntry: 'app-saved-lead-form',
      ),
    ]);
    final lead = entries.singleWhere(
      (e) => e.launchKey == 'intochat.saved-lead-form',
    );
    expect(lead.title, 'Lead form');
    expect(lead.subtitle, 'Enter a lead');
    expect(entries.map((e) => e.launchKey), contains('files'));
  });

  test('a non-declarative catalog app is not offered', () {
    final entries = launcherEntries(const [
      AppManifestSummary(
        id: 'intochat.assistant',
        name: 'Assistant',
        description: 'Answers questions',
        kind: 'prompt',
      ),
    ]);
    expect(entries.map((e) => e.launchKey), ['files', 'images']);
  });
}
