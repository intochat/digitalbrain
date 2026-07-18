import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/surface/ui_surface_models.dart';

void main() {
  final gateway = BrainGateway(
    httpBase: 'http://localhost:5320',
    wsBase: 'ws://localhost:5320',
  );

  test('fetch surface snapshot from live gateway', () async {
    final snapshot = await gateway.fetchSnapshot('group-chat');
    expect(snapshot.surface.surfaceId, isNotEmpty);
    expect(snapshot.surface.revision, greaterThanOrEqualTo(0));
  });

  test('watch delivers a surface feed message', () async {
    final message = await gateway
        .watch(cursor: 0)
        .first
        .timeout(const Duration(seconds: 10));
    expect(message.schemaVersion, UiFeedMessage.supportedSchemaVersion);
    expect(message.sequence, greaterThanOrEqualTo(0));
  });
}
