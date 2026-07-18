import 'package:fixnum/fixnum.dart';

import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../grpc/ui.pbenum.dart' as wire_enums;
import '../../runtime/runtime_errors.dart';

enum FeatureCatalogStatus { draft, installed }

class FeatureCatalogItem {
  const FeatureCatalogItem({
    required this.draftId,
    required this.goal,
    required this.status,
    required this.revision,
    required this.installationId,
  });

  final String draftId;
  final String goal;
  final FeatureCatalogStatus status;
  final Int64 revision;
  final String? installationId;
}

abstract interface class FeatureCatalogGateway {
  Future<List<FeatureCatalogItem>> loadFeatures();
}

class GrpcFeatureCatalogGateway implements FeatureCatalogGateway {
  const GrpcFeatureCatalogGateway({required FeatureCatalogClient client})
    : _client = client;

  final FeatureCatalogClient _client;

  @override
  Future<List<FeatureCatalogItem>> loadFeatures() async {
    final reply = await _client.listFeatures(wire.ListFeaturesRequest());
    try {
      return List.unmodifiable(reply.features.map(_mapFeature));
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException(
        'Feature catalog response could not be verified.',
      );
    }
  }
}

FeatureCatalogItem _mapFeature(wire.FeatureDraft draft) {
  if (draft.draftId.trim().isEmpty ||
      draft.goal.trim().isEmpty ||
      draft.revision < Int64.ZERO ||
      draft.revision == Int64.ZERO &&
          draft.status ==
              wire_enums.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED) {
    throw const ProtocolException('Feature catalog item is incomplete.');
  }
  final status = switch (draft.status) {
    wire_enums.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT =>
      FeatureCatalogStatus.draft,
    wire_enums.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED =>
      FeatureCatalogStatus.installed,
    _ => throw const ProtocolException('Feature catalog status is invalid.'),
  };
  return FeatureCatalogItem(
    draftId: draft.draftId,
    goal: draft.goal,
    status: status,
    revision: draft.revision,
    installationId: draft.hasInstallationId() ? draft.installationId : null,
  );
}
