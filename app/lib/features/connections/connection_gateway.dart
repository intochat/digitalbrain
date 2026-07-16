import '../../core/session/digitalbrain_client.dart';
import '../../grpc/endpoint.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../../grpc/ui.pbenum.dart' as wire_enums;
import '../../runtime/runtime_errors.dart';
import 'connection_models.dart';

abstract interface class ConnectionGateway {
  Future<List<ConnectionItem>> loadConnections();
}

abstract interface class ConnectionDetailGateway {
  Future<ConnectionItem> loadConnection(String connectionId);
}

class GrpcConnectionGateway
    implements ConnectionGateway, ConnectionDetailGateway {
  const GrpcConnectionGateway({required ConnectionClient client})
    : _client = client;

  final ConnectionClient _client;

  @override
  Future<List<ConnectionItem>> loadConnections() async {
    final reply = await _client.listConnections(wire.ListConnectionsRequest());
    try {
      return List.unmodifiable(reply.connections.map(_mapConnection));
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException(
        'Connections response could not be verified.',
      );
    }
  }

  @override
  Future<ConnectionItem> loadConnection(String connectionId) async {
    _requireIdentity(connectionId, 'connectionId', 256);
    final reply = await _client.getConnection(
      wire.GetConnectionRequest(connectionId: connectionId),
    );
    try {
      if (!reply.hasConnection() ||
          reply.connection.connectionId != connectionId) {
        throw const ProtocolException('Connection response is incomplete.');
      }
      return _mapConnection(reply.connection);
    } on ProtocolException {
      rethrow;
    } on Object {
      throw const ProtocolException(
        'Connection response could not be verified.',
      );
    }
  }
}

Uri resolveConnectionConnectUri(String connectPath) {
  if (connectPath.isEmpty ||
      !connectPath.startsWith('/') ||
      connectPath.contains('://')) {
    throw ArgumentError.value(connectPath, 'connectPath', 'Invalid path.');
  }
  return Uri.parse(resolveKernelCallbackUrl(connectPath));
}

ConnectionItem _mapConnection(wire.ConnectionSnapshot value) {
  try {
    final health = switch (value.health) {
      wire_enums.ConnectionHealthStatus.CONNECTION_HEALTH_STATUS_HEALTHY =>
        ConnectionHealth.healthy,
      wire_enums
          .ConnectionHealthStatus
          .CONNECTION_HEALTH_STATUS_NEEDS_REAUTH =>
        ConnectionHealth.needsReauth,
      wire_enums
          .ConnectionHealthStatus
          .CONNECTION_HEALTH_STATUS_DISCONNECTED =>
        ConnectionHealth.disconnected,
      wire_enums
          .ConnectionHealthStatus
          .CONNECTION_HEALTH_STATUS_MISCONFIGURED =>
        ConnectionHealth.misconfigured,
      _ => throw const ProtocolException('Connection health is invalid.'),
    };
    final healthDetail = value.hasHealthDetail() ? value.healthDetail : null;
    final connectPath = value.hasConnectPath() ? value.connectPath : null;
    final capabilityIds = health == ConnectionHealth.healthy
        ? List<String>.unmodifiable(value.unlockedCapabilityIds)
        : const <String>[];
    return ConnectionItem(
      provider: value.provider,
      connectionId: value.connectionId,
      displayName: value.displayName,
      health: health,
      healthDetail: healthDetail,
      unlockedCapabilityIds: capabilityIds,
      connectPath: connectPath,
    );
  } on ProtocolException {
    rethrow;
  } on Object {
    throw const ProtocolException(
      'Connection response could not be verified.',
    );
  }
}

void _requireIdentity(String value, String name, int maximumLength) {
  if (value.isEmpty ||
      value.length > maximumLength ||
      value.trim() != value ||
      value.runes.any(
        (character) => character < 32 || character >= 127 && character <= 159,
      )) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}
