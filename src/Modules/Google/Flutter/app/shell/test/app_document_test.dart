import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_document.dart';

void main() {
  test('block identity and links survive independent editing', () {
    final d = AppDocument.fromJson({'version': 1, 'preamble': 'App', 'behaviors': [{'id': 'b', 'title': 'Read', 'description': 'Read pages', 'sourcePaths': ['behaviors/read.cs'], 'scenarioIds': ['s']}], 'scenarios': [{'id': 's', 'name': 'Reads pages', 'body': 'Reads.', 'isLive': false}]});
    final copy = d.behaviors.single.duplicate('new');
    expect(copy.id, 'new');
    expect(copy.sourcePaths, isEmpty);
    expect(copy.scenarioIds, isEmpty);
    expect(AppDocument.fromJson(d.toJson()).behaviors.single.scenarioIds, ['s']);
  });
  test('future document versions are refused by the editing model', () {
    expect(() => AppDocument.fromJson({'version': 99}), throwsFormatException);
  });
}

