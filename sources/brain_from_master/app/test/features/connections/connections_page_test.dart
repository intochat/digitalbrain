import 'dart:async';

import 'package:digitalbrain_flutter/features/connections/connection_gateway.dart';
import 'package:digitalbrain_flutter/features/connections/connection_models.dart';
import 'package:digitalbrain_flutter/features/connections/connections_page.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/grpc/ui.pbenum.dart' as wire_enums;
import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('shows empty intro when no Connections are available', (
    tester,
  ) async {
    await _pumpPage(
      tester,
      ConnectionsPage(gateway: _StubGateway([])),
    );
    await tester.pumpAndSettle();

    expect(find.text('No Connections yet'), findsOneWidget);
    expect(
      find.text('Connect apps to unlock capabilities for Features.'),
      findsOneWidget,
    );
    expect(find.textContaining('Agent'), findsNothing);
    expect(find.textContaining('MCP'), findsNothing);
  });

  testWidgets('shows error state with retry', (tester) async {
    final gateway = _QueueGateway();
    await _pumpPage(tester, ConnectionsPage(gateway: gateway));

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    gateway.requests.single.completeError(StateError('offline'));
    await tester.pumpAndSettle();

    expect(find.text('Connections could not be loaded.'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);

    await tester.tap(find.text('Retry'));
    await tester.pump();
    expect(gateway.requests, hasLength(2));
    gateway.requests.last.complete([
      _connection(
        displayName: 'Google',
        health: ConnectionHealth.disconnected,
        connectPath: '/oauth/start/google',
      ),
    ]);
    await tester.pumpAndSettle();

    expect(find.text('Google'), findsOneWidget);
    expect(find.text('Disconnected'), findsOneWidget);
  });

  testWidgets(
    'renders healthy Connection with unlocked capabilities and no Connect CTA',
    (tester) async {
      await _pumpPage(
        tester,
        ConnectionsPage(
          gateway: _StubGateway([
            _connection(
              displayName: 'Google',
              health: ConnectionHealth.healthy,
              unlockedCapabilityIds: const ['gmail.read', 'calendar.read'],
              connectPath: '/oauth/start/google',
            ),
          ]),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Google'), findsOneWidget);
      expect(find.text('Healthy'), findsOneWidget);
      expect(find.text('gmail.read'), findsOneWidget);
      expect(find.text('calendar.read'), findsOneWidget);
      expect(find.text('Connect'), findsNothing);
      expect(find.text('Reconnect'), findsNothing);
    },
  );

  testWidgets(
    'Connect opens Edge origin + connect_path and hides capabilities when unhealthy',
    (tester) async {
      Uri? opened;
      await _pumpPage(
        tester,
        ConnectionsPage(
          gateway: _StubGateway([
            _connection(
              displayName: 'Google',
              health: ConnectionHealth.disconnected,
              unlockedCapabilityIds: const ['gmail.read'],
              connectPath: '/oauth/start/google',
            ),
          ]),
          resolveConnectUri: (path) =>
              Uri.parse('https://edge.example$path'),
          onConnect: (uri) => opened = uri,
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Disconnected'), findsOneWidget);
      expect(find.text('gmail.read'), findsNothing);
      expect(find.text('Connect'), findsOneWidget);

      await tester.tap(find.text('Connect'));
      await tester.pump();

      expect(opened, Uri.parse('https://edge.example/oauth/start/google'));
      expect(opened.toString().contains('mcp'), isFalse);
    },
  );

  testWidgets('needs reauth shows Reconnect CTA', (tester) async {
    Uri? opened;
    await _pumpPage(
      tester,
      ConnectionsPage(
        gateway: _StubGateway([
          _connection(
            displayName: 'Salesforce',
            health: ConnectionHealth.needsReauth,
            connectPath: '/oauth/start/salesforce',
          ),
        ]),
        resolveConnectUri: (path) => Uri.parse('https://edge.example$path'),
        onConnect: (uri) => opened = uri,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Needs reauth'), findsOneWidget);
    await tester.tap(find.text('Reconnect'));
    await tester.pump();
    expect(opened?.path, '/oauth/start/salesforce');
  });

  test('gateway maps wire snapshots and drops capabilities when unhealthy',
      () async {
    final gateway = GrpcConnectionGateway(
      client: _ConnectionClient([
        wire.ConnectionSnapshot(
          provider: 'google',
          connectionId: 'conn-google',
          displayName: 'Google',
          health: wire_enums
              .ConnectionHealthStatus
              .CONNECTION_HEALTH_STATUS_HEALTHY,
          unlockedCapabilityIds: const ['gmail.read'],
          connectPath: '/oauth/start/google',
        ),
        wire.ConnectionSnapshot(
          provider: 'salesforce',
          connectionId: 'conn-salesforce',
          displayName: 'Salesforce',
          health: wire_enums
              .ConnectionHealthStatus
              .CONNECTION_HEALTH_STATUS_DISCONNECTED,
          healthDetail: 'Not linked',
          unlockedCapabilityIds: const ['crm.read'],
          connectPath: '/oauth/start/salesforce',
        ),
      ]),
    );

    final items = await gateway.loadConnections();

    expect(items, hasLength(2));
    expect(items.first.displayName, 'Google');
    expect(items.first.health, ConnectionHealth.healthy);
    expect(items.first.unlockedCapabilityIds, ['gmail.read']);
    expect(items.first.visibleCapabilityIds, ['gmail.read']);
    expect(items.last.health, ConnectionHealth.disconnected);
    expect(items.last.healthDetail, 'Not linked');
    expect(items.last.unlockedCapabilityIds, isEmpty);
    expect(items.last.visibleCapabilityIds, isEmpty);
    expect(items.last.connectPath, '/oauth/start/salesforce');
    expect(items.last.canConnect, isTrue);
  });
}

Future<void> _pumpPage(WidgetTester tester, Widget page) async {
  await tester.pumpWidget(MaterialApp(home: page));
}

ConnectionItem _connection({
  String provider = 'google',
  String connectionId = 'conn-google',
  required String displayName,
  required ConnectionHealth health,
  String? healthDetail,
  List<String> unlockedCapabilityIds = const [],
  String? connectPath,
}) => ConnectionItem(
  provider: provider,
  connectionId: connectionId,
  displayName: displayName,
  health: health,
  healthDetail: healthDetail,
  unlockedCapabilityIds: unlockedCapabilityIds,
  connectPath: connectPath,
);

final class _StubGateway implements ConnectionGateway {
  _StubGateway(this.items);

  final List<ConnectionItem> items;

  @override
  Future<List<ConnectionItem>> loadConnections() async => items;
}

final class _QueueGateway implements ConnectionGateway {
  final List<Completer<List<ConnectionItem>>> requests = [];

  @override
  Future<List<ConnectionItem>> loadConnections() {
    final completer = Completer<List<ConnectionItem>>();
    requests.add(completer);
    return completer.future;
  }
}

final class _ConnectionClient implements ConnectionClient {
  _ConnectionClient(this.connections);

  final List<wire.ConnectionSnapshot> connections;

  @override
  Future<wire.ListConnectionsReply> listConnections(
    wire.ListConnectionsRequest request,
  ) async => wire.ListConnectionsReply(connections: connections);

  @override
  Future<wire.ConnectionReply> getConnection(
    wire.GetConnectionRequest request,
  ) async {
    final match = connections.where(
      (item) => item.connectionId == request.connectionId,
    );
    if (match.isEmpty) return wire.ConnectionReply();
    return wire.ConnectionReply(connection: match.first);
  }
}
