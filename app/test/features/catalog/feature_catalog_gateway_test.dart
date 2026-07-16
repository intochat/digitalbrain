import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/catalog/feature_catalog_gateway.dart';
import 'package:digitalbrain_flutter/features/catalog/feature_catalog_page.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/grpc/ui.pbenum.dart' as wire_enums;

void main() {
  test('maps persisted Feature Drafts into catalog items', () async {
    final gateway = GrpcFeatureCatalogGateway(
      client: _CatalogClient([
        wire.FeatureDraft(
          draftId: 'draft-1',
          goal: 'Enrich a Salesforce account from email',
          status: wire_enums.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT,
          revision: Int64(2),
        ),
      ]),
    );

    final features = await gateway.loadFeatures();

    expect(features, hasLength(1));
    expect(features.single.draftId, 'draft-1');
    expect(features.single.goal, 'Enrich a Salesforce account from email');
    expect(features.single.status, FeatureCatalogStatus.draft);
    expect(features.single.revision, Int64(2));
  });

  testWidgets('opens a persisted Feature in Studio', (tester) async {
    var openedDraftId = '';
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureCatalogPage(
          gateway: GrpcFeatureCatalogGateway(
            client: _CatalogClient([
              wire.FeatureDraft(
                draftId: 'draft-2',
                goal: 'Build a useful Feature',
                status:
                    wire_enums.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT,
                revision: Int64(1),
              ),
            ]),
          ),
          onOpenFeature: (draftId) => openedDraftId = draftId,
          onCreateFeature: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Build a useful Feature'), findsOneWidget);
    await tester.tap(find.text('Build a useful Feature'));

    expect(openedDraftId, 'draft-2');
  });
}

final class _CatalogClient implements FeatureCatalogClient {
  _CatalogClient(this.drafts);

  final List<wire.FeatureDraft> drafts;

  @override
  Future<wire.ListFeaturesReply> listFeatures(
    wire.ListFeaturesRequest request,
  ) async => wire.ListFeaturesReply(features: drafts);
}
