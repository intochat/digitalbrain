import 'package:digitalbrain_flutter/grpc/ui.pbgrpc.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('generated client binds every authoritative product RPC', () {
    final members = <Object Function(DigitalBrainV2UiClient)>[
      (client) => client.getFeatureDraft,
      (client) => client.reviseFeatureDraft,
      (client) => client.suggestFeatureChange,
      (client) => client.verifyFeatureDraft,
      (client) => client.installFeatureVersion,
      (client) => client.resumeOriginatingRequest,
      (client) => client.listFeatures,
      (client) => client.getFeature,
      (client) => client.listConnections,
      (client) => client.getConnection,
      (client) => client.listActivity,
      (client) => client.getRun,
      (client) => client.listMemoryItems,
      (client) => client.getMemoryItem,
      (client) => client.getHomeSummary,
    ];

    expect(members, hasLength(15));
  });
}
