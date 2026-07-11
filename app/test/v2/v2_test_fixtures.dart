import 'dart:convert';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';

final DateTime v2TestNow = DateTime.utc(2035, 1, 1);

V2SessionIdentity testIdentity({
  String tenant = 'tenant-a',
  String workspace = 'workspace-a',
  String principal = 'principal-a',
  String session = 'session-a',
}) => V2SessionIdentity(
  sessionId: session,
  tenantId: tenant,
  workspaceId: workspace,
  principalId: principal,
);

V2SessionBundle testSession({
  String accessToken = 'access-token',
  String refreshToken = 'refresh-token',
  V2SessionIdentity? identity,
  DateTime? accessExpiresAt,
  DateTime? refreshExpiresAt,
}) => V2SessionBundle(
  identity: identity ?? testIdentity(),
  credentials: V2SessionCredentials(
    accessToken: accessToken,
    refreshToken: refreshToken,
    accessExpiresAt:
        accessExpiresAt ?? v2TestNow.add(const Duration(minutes: 15)),
    refreshExpiresAt:
        refreshExpiresAt ?? v2TestNow.add(const Duration(days: 1)),
  ),
);

Map<String, Object?> surfaceJsonMap({
  int sequence = 1,
  int revision = 1,
  String surfaceId = 'surface-main',
  String tenant = 'tenant-a',
  String workspace = 'workspace-a',
  String audienceKind = 'principal',
  String audienceId = 'principal-a',
  DateTime? expiresAt,
  Map<String, Object?>? payload,
  List<Map<String, Object?>>? actions,
}) => <String, Object?>{
  'protocolVersion': 2,
  'surfaceSchema': 'digitalbrain.surface',
  'surfaceSchemaVersion': 2,
  'surfaceId': surfaceId,
  'revision': revision,
  'tenantId': tenant,
  'workspaceId': workspace,
  'audience': {'kind': audienceKind, 'id': audienceId},
  'feedSequence': sequence,
  'createdAt': v2TestNow.toIso8601String(),
  'expiresAt': (expiresAt ?? v2TestNow.add(const Duration(hours: 1)))
      .toIso8601String(),
  'correlationId': 'correlation-a',
  'cause': {'kind': 'event', 'id': 'event-a'},
  'requiredClientCapabilities': <String>[],
  'contentHash': List.filled(64, 'a').join(),
  'payload':
      payload ??
      <String, Object?>{
        'kind': 'native',
        'nativeKind': 'message',
        'data': {'title': 'V2 ready', 'message': 'Authenticated surface'},
      },
  'actions': actions ?? <Map<String, Object?>>[],
};

Map<String, Object?> testActionJson({
  String bindingId = 'refresh-binding',
  String actionType = 'ui.surface.refresh',
  String actionToken = 'signed-action-token',
  String surfaceId = 'surface-main',
  int surfaceRevision = 1,
}) => <String, Object?>{
  'actionSchemaVersion': 1,
  'bindingId': bindingId,
  'actionType': actionType,
  'actionToken': actionToken,
  'surfaceId': surfaceId,
  'surfaceRevision': surfaceRevision,
  'expiresAt': v2TestNow.add(const Duration(minutes: 5)).toIso8601String(),
};

Map<String, Object?> testInoActionJson({
  String actionToken = 'signed-ino-action-token',
  String surfaceId = 'surface-main',
  int surfaceRevision = 1,
}) => testActionJson(
  bindingId: 'ino.send',
  actionType: 'ino.interact',
  actionToken: actionToken,
  surfaceId: surfaceId,
  surfaceRevision: surfaceRevision,
);

Map<String, Object?> inoConversationPayload({
  String intro = 'Ask INO about this workspace.',
  List<Map<String, Object?>> messages = const [],
  Map<String, Object?>? operation,
}) => <String, Object?>{
  'kind': 'native',
  'nativeKind': 'inoConversation',
  'data': {'intro': intro, 'messages': messages, 'operation': operation},
};

Map<String, Object?> inoMessage({
  required String role,
  required String text,
  required String state,
  String? turnKey,
}) => <String, Object?>{
  'turnKey':
      turnKey ?? 'turn-$role-${text.hashCode.toUnsigned(32).toRadixString(16)}',
  'role': role,
  'text': text,
  'state': state,
};

Map<String, Object?> inoOperation({
  required String state,
  bool retryable = false,
  String? safeReason,
  Map<String, Object?>? action,
}) => <String, Object?>{
  'state': state,
  'retryable': retryable,
  'safeReason': ?safeReason,
  'action': ?action,
};

Map<String, Object?> googleConnectionAction({
  String target = 'https://accounts.google.com/o/oauth2/v2/auth?state=test',
}) => <String, Object?>{
  'kind': 'openUrl',
  'label': 'Connect Google',
  'target': target,
};

Map<String, Object?> salesforceConnectionAction({
  String target =
      'http://localhost:51014/oauth/start/salesforce?t=opaque-token',
}) => <String, Object?>{
  'kind': 'openUrl',
  'label': 'Connect Salesforce',
  'target': target,
};

String surfaceJsonString({
  int sequence = 1,
  int revision = 1,
  String surfaceId = 'surface-main',
  String tenant = 'tenant-a',
  String workspace = 'workspace-a',
  String audienceKind = 'principal',
  String audienceId = 'principal-a',
  DateTime? expiresAt,
  Map<String, Object?>? payload,
  List<Map<String, Object?>>? actions,
}) => jsonEncode(
  surfaceJsonMap(
    sequence: sequence,
    revision: revision,
    surfaceId: surfaceId,
    tenant: tenant,
    workspace: workspace,
    audienceKind: audienceKind,
    audienceId: audienceId,
    expiresAt: expiresAt,
    payload: payload,
    actions: actions,
  ),
);

SurfaceEnvelope testSurface({
  int sequence = 1,
  int revision = 1,
  String surfaceId = 'surface-main',
  String tenant = 'tenant-a',
  String workspace = 'workspace-a',
  String audienceKind = 'principal',
  String audienceId = 'principal-a',
  DateTime? expiresAt,
  Map<String, Object?>? payload,
  List<Map<String, Object?>>? actions,
}) => const SurfaceEnvelopeDecoder().decode(
  surfaceJsonString(
    sequence: sequence,
    revision: revision,
    surfaceId: surfaceId,
    tenant: tenant,
    workspace: workspace,
    audienceKind: audienceKind,
    audienceId: audienceId,
    expiresAt: expiresAt,
    payload: payload,
    actions: actions,
  ),
);
