import 'package:digitalbrain_flutter/features/catalog/feature_catalog_gateway.dart';
import 'package:digitalbrain_flutter/features/catalog/feature_catalog_page.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'empty state asks to install a Feature specialist with template goal',
    (tester) async {
      var createTapped = false;
      var connectionsTapped = false;
      await _pumpPage(
        tester,
        FeatureCatalogPage(
          gateway: _StubGateway(const []),
          onOpenFeature: (_) {},
          onCreateFeature: () => createTapped = true,
          onOpenConnections: () => connectionsTapped = true,
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Install a Feature specialist'), findsOneWidget);
      expect(
        find.textContaining('Enrich Salesforce account from Gmail'),
        findsOneWidget,
      );
      expect(find.text('Create Feature'), findsWidgets);
      expect(find.text('Connect apps'), findsOneWidget);
      expect(find.textContaining('Agent'), findsNothing);
      expect(find.textContaining('MCP'), findsNothing);
      expect(find.textContaining('marketplace'), findsNothing);

      await tester.tap(find.text('Create Feature').last);
      expect(createTapped, isTrue);

      await tester.tap(find.text('Connect apps'));
      expect(connectionsTapped, isTrue);
    },
  );

  testWidgets('cards show Draft and Installed status as Feature specialists', (
    tester,
  ) async {
    await _pumpPage(
      tester,
      FeatureCatalogPage(
        gateway: _StubGateway([
          FeatureCatalogItem(
            draftId: 'draft-1',
            goal: 'Draft enrichment Feature',
            status: FeatureCatalogStatus.draft,
            revision: Int64(1),
            installationId: null,
          ),
          FeatureCatalogItem(
            draftId: 'installed-1',
            goal: 'Installed enrichment Feature',
            status: FeatureCatalogStatus.installed,
            revision: Int64(3),
            installationId: 'install-1',
          ),
        ]),
        onOpenFeature: (_) {},
        onCreateFeature: () {},
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Draft enrichment Feature'), findsOneWidget);
    expect(find.text('Installed enrichment Feature'), findsOneWidget);
    expect(find.text('Draft'), findsOneWidget);
    expect(find.text('Installed'), findsOneWidget);
    expect(find.text('Feature specialist'), findsNWidgets(2));
    expect(find.text('Version 1'), findsOneWidget);
    expect(find.text('Version 3'), findsOneWidget);
    expect(find.textContaining('Agent'), findsNothing);
    expect(find.textContaining('MCP'), findsNothing);
  });

  testWidgets('hides Connect apps when onOpenConnections is omitted', (
    tester,
  ) async {
    await _pumpPage(
      tester,
      FeatureCatalogPage(
        gateway: _StubGateway(const []),
        onOpenFeature: (_) {},
        onCreateFeature: () {},
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Install a Feature specialist'), findsOneWidget);
    expect(find.text('Connect apps'), findsNothing);
  });
}

Future<void> _pumpPage(WidgetTester tester, Widget page) {
  return tester.pumpWidget(MaterialApp(home: page));
}

final class _StubGateway implements FeatureCatalogGateway {
  _StubGateway(this.items);

  final List<FeatureCatalogItem> items;

  @override
  Future<List<FeatureCatalogItem>> loadFeatures() async => items;
}
