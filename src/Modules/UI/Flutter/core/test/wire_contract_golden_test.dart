import 'dart:convert';
import 'dart:io';

import 'package:test/test.dart';

void main() {
  test('flutter wire contracts golden matches C# contracts', () {
    // core/ package root is src/Modules/UI/Flutter/core; the canonical golden file lives
    // in the sibling contracts project two levels up, at
    // src/Modules/UI/DigitalBrain.Modules.UI.Contracts/. A second, stale copy used to live
    // at src/Contracts/ (deleted) -- this must resolve only the canonical one, or the rich
    // C#-authored schema below never actually gets checked.
    final goldenUri = Directory.current.uri.resolve(
      '../../DigitalBrain.Modules.UI.Contracts/flutter-wire-contracts.golden.json',
    );
    final golden = File.fromUri(goldenUri);
    expect(
      golden.existsSync(),
      isTrue,
      reason: 'flutter-wire-contracts.golden.json not found at $goldenUri',
    );

    final manifest =
        jsonDecode(golden.readAsStringSync()) as Map<String, Object?>;
    expect(manifest['namespace'], 'DigitalBrain.UI');
    expect(manifest['version'], 1);

    final types = (manifest['types'] as List).cast<Map<String, Object?>>();
    final aliases = types.map((t) => t['alias']).toSet();
    expect(
      aliases,
      containsAll([
        'ui.surface',
        'ui.open-surface',
        'ui.surface-opened',
        'ui.control-activated',
        'ui.kit-card',
        'ui.chart-state',
        'ui.chart-point',
        'ui.image-state',
        'excel.workbook-state',
        'excel.row',
      ]),
    );

    Map<String, Object?> typeNamed(String alias) =>
        types.firstWhere((t) => t['alias'] == alias, orElse: () => {});

    Set<String> propertyNamesOf(String alias) =>
        (typeNamed(alias)['properties'] as List? ?? [])
            .cast<Map<String, Object?>>()
            .map((p) => p['name'] as String)
            .toSet();

    // Shape assertions pin each alias's property names to the C# record they mirror, so a
    // renamed/added/removed property on either side fails this test instead of silently
    // drifting. Property names are asserted exactly as the golden file spells them
    // (PascalCase, matching the C# [property: Id(n)] members).
    expect(
      propertyNamesOf('ui.kit-card'),
      {'Kind', 'Name', 'Caption'},
      reason: 'ui.kit-card must mirror KitCardOffer(Kind, Name, Caption)',
    );
    expect(
      propertyNamesOf('ui.chart-state'),
      {'Title', 'ChartKind', 'Points'},
      reason: 'ui.chart-state must mirror ChartState(Title, ChartKind, Points)',
    );
    expect(
      propertyNamesOf('ui.chart-point'),
      {'Label', 'Value'},
      reason: 'ui.chart-point must mirror ChartPoint(Label, Value)',
    );
    expect(
      propertyNamesOf('ui.image-state'),
      {'Prompt', 'Model', 'MediaType', 'BlobName'},
      reason:
          'ui.image-state must mirror ImageState(Prompt, Model, MediaType, BlobName)',
    );
    expect(
      propertyNamesOf('excel.workbook-state'),
      {'Title', 'SheetName', 'Columns', 'Rows'},
      reason:
          'excel.workbook-state must mirror ExcelState(Title, SheetName, Columns, Rows)',
    );
    expect(
      propertyNamesOf('excel.row'),
      {'Cells'},
      reason: 'excel.row must mirror ExcelRow(Cells)',
    );
  });
}
