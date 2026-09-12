import 'dart:convert';

import 'package:archive/archive.dart';
import 'package:digitalbrain_flutter_shell/workspace/artifact_editors.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_table_import.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:markdraw/markdraw.dart' as markdraw;

void main() {
  test(
    'CSV import preserves quoted multiline text and numeric identifiers',
    () {
      final parsed = parseWorkspaceTable(
        'Id,Notes\r\n001,"Hello, ""Sam""\nNext line"\r\n',
      );
      expect(parsed.rows.single.cells, ['001', 'Hello, "Sam"\nNext line']);
      expect(() => parseWorkspaceTable('A,B\nonly one'), throwsFormatException);
      expect(() => parseWorkspaceTable('A\n"unclosed'), throwsFormatException);
    },
  );

  test(
    'XLSX import follows first worksheet and expands sparse shared strings',
    () {
      final zip = Archive()
        ..addFile(
          ArchiveFile.string(
            'xl/workbook.xml',
            '<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="First" r:id="rId2"/></sheets></workbook>',
          ),
        )
        ..addFile(
          ArchiveFile.string(
            'xl/_rels/workbook.xml.rels',
            '<Relationships><Relationship Id="rId2" Target="worksheets/sheet2.xml"/></Relationships>',
          ),
        )
        ..addFile(
          ArchiveFile.string(
            'xl/sharedStrings.xml',
            '<sst><si><t>Account</t></si><si><t>0012</t></si></sst>',
          ),
        )
        ..addFile(
          ArchiveFile.string(
            'xl/worksheets/sheet2.xml',
            '<worksheet><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>Name</t></is></c></row><row r="2"><c r="A2" t="s"><v>1</v></c></row></sheetData></worksheet>',
          ),
        );
      final parsed = parseWorkspaceXlsx(ZipEncoder().encode(zip));
      expect(parsed.columns.map((c) => c.label), ['Account', 'Name']);
      expect(parsed.rows.single.cells, ['0012', '']);
    },
  );
  test('Markdraw dependency loads and round trips actual editable geometry', () {
    final controller = markdraw.MarkdrawController();
    final reopened = markdraw.MarkdrawController();
    addTearDown(controller.dispose);
    addTearDown(reopened.dispose);
    const source =
        '---\nmarkdraw: 1\n---\n\n```sketch\nrect "Contact" id=contact at 100,100 size 160x80\n```\n';
    controller.loadFromContent(source, 'diagram.markdraw');
    final serialized = controller.serializeScene();
    expect(serialized, contains('Contact'));
    expect(serialized, contains('rect'));
    reopened.loadFromContent(serialized, 'diagram.markdraw');
    expect(reopened.serializeScene(), serialized);
  });

  test('image adjustments retain original bytes across persistence', () {
    final original = base64Encode([1, 2, 3, 4]);
    final artifact = WorkspaceArtifact(
      id: 'image',
      title: 'Image',
      kind: 'image',
      data: {'originalDataUrl': 'data:image/png;base64,$original'},
      viewState: {'brightness': .5, 'quarterTurns': 1},
    );
    final restored = WorkspaceArtifact.fromJson(
      jsonDecode(jsonEncode(artifact.toJson())),
    );
    expect(workspaceImageBytes(restored), [1, 2, 3, 4]);
    expect(restored.viewState['quarterTurns'], 1);
    expect(workspaceBrightnessMatrix(.5)[4], 127.5);
    expect(workspaceBrightnessMatrix(0)[4], 0);
  });

  testWidgets('chart artifacts draw with the shared chart control', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: WorkspaceArtifactEditor(
            artifact: WorkspaceArtifact(
              id: 'chart-by-country',
              title: 'Companies by country',
              kind: 'chart',
              data: {
                'kind': 'chart',
                'id': 'chart-by-country',
                'title': 'Companies by country',
                'chartKind': 'bar',
                'points': [
                  {'label': 'GB', 'value': 6},
                  {'label': 'DE', 'value': 3},
                ],
                'message': 'Chart saved.',
              },
            ),
            onChanged: (_) {},
          ),
        ),
      ),
    );
    await tester.pump();
    final chart = tester.widget<UiChart>(find.byType(UiChart));
    expect(chart.part.title, 'Companies by country');
    expect(chart.part.chartKind, 'bar');
    expect(chart.part.points.map((p) => p.label), ['GB', 'DE']);
    expect(find.text('This artifact type cannot be edited yet.'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('image rotate and reset emit persistent view state', (
    tester,
  ) async {
    var artifact = WorkspaceArtifact(
      id: 'image',
      title: 'Image',
      kind: 'image',
      data: {
        'dataUrl': 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1cAAAAASUVORK5CYII=',
      },
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: StatefulBuilder(
            builder: (context, setState) => WorkspaceArtifactEditor(
              artifact: artifact,
              onChanged: (value) => setState(() => artifact = value),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.byTooltip('Rotate right'));
    await tester.pump();
    expect(artifact.viewState['quarterTurns'], 1);
    await tester.tap(find.text('Reset'));
    await tester.pump();
    expect(artifact.viewState['quarterTurns'], 0);
    expect(artifact.data['dataUrl'], startsWith('data:image/png'));
  });
}
