import 'dart:convert';
import 'dart:io';

import 'package:digitalbrain_flutter/grpc/ui.pbgrpc.dart';
import 'package:fixnum/fixnum.dart';
import 'package:grpc/grpc.dart';

Future<void> main() async {
  final host = Platform.environment['DIGITALBRAIN_UI_HOST'] ?? 'localhost';
  final port =
      int.tryParse(Platform.environment['DIGITALBRAIN_UI_PORT'] ?? '') ?? 5000;
  final useTls =
      (Platform.environment['DIGITALBRAIN_UI_TLS'] ?? 'false').toLowerCase() ==
      'true';
  final channel = ClientChannel(
    host,
    port: port,
    options: ChannelOptions(
      credentials: useTls
          ? const ChannelCredentials.secure(
              onBadCertificate: allowBadCertificates,
            )
          : const ChannelCredentials.insecure(),
    ),
  );
  final client = DigitalBrainV2UiClient(channel);
  final expectedMarker =
      Platform.environment['DIGITALBRAIN_EXPECT_MARKER']?.trim() ?? '';
  var surfaceCount = 0;
  var markerFound = false;
  try {
    final session = await client.bootstrapSession(
      BootstrapSessionRequest(username: 'admin', password: 'admin'),
      options: CallOptions(
        metadata: const {'x-v2-audience': 'digitalbrain-v3-ui'},
        timeout: const Duration(seconds: 10),
      ),
    );
    stdout.writeln(
      'SESSION actor=${session.actorId} owner=${session.ownerId} '
      'session=${session.sessionId}',
    );
    final response = client.watchSurfaceFeed(
      WatchSurfaceFeedRequest(
        afterSequence: Int64.ZERO,
        audience: FeedAudienceKind.FEED_AUDIENCE_KIND_ACTOR,
        clientCapabilities: const {
          'ui.protocol.v2',
          'ui.widget-vocabulary.v2',
          'ui.payload.widgetTree',
          'ui.payload.rfw',
          'ui.payload.native',
          'ui.native.typed-actions',
          'ui.native.feed-reset',
          'ui.native.feed-ack',
          'ui.native.ino-conversation',
          'ui.native.feature-approval',
        },
        maxBatchSize: 50,
      ),
      options: CallOptions(
        metadata: {
          'x-v2-session': session.accessToken,
          'x-v2-audience': 'digitalbrain-v3-ui',
        },
      ),
    );
    await for (final event in response.timeout(
      const Duration(seconds: 8),
      onTimeout: (sink) => sink.close(),
    )) {
      switch (event.whichEvent()) {
        case SurfaceFeedEvent_Event.surfaceJson:
          final map = jsonDecode(event.surfaceJson) as Map<String, Object?>;
          final containsMarker =
              expectedMarker.isNotEmpty &&
              event.surfaceJson.contains(expectedMarker);
          if (expectedMarker.isEmpty || containsMarker) {
            _printSurface('SURFACE', map);
          }
          surfaceCount++;
          markerFound = markerFound || containsMarker;
        case SurfaceFeedEvent_Event.reset:
          stdout.writeln(
            'RESET reason=${event.reset.reason} '
            'resume=${event.reset.resumeSequence} '
            'snapshots=${event.reset.snapshotJson.length}',
          );
          for (final snapshot in event.reset.snapshotJson) {
            final containsMarker =
                expectedMarker.isNotEmpty && snapshot.contains(expectedMarker);
            surfaceCount++;
            markerFound = markerFound || containsMarker;
            if (expectedMarker.isEmpty || containsMarker) {
              _printSurface(
                'SNAPSHOT',
                jsonDecode(snapshot) as Map<String, Object?>,
              );
            }
          }
        case SurfaceFeedEvent_Event.notSet:
          throw StateError('Surface feed returned an empty event.');
      }
    }
    await response.cancel();
    stdout.writeln(
      'SUMMARY surfaces=$surfaceCount markerFound=$markerFound '
      'expectedMarker=${expectedMarker.isEmpty ? '<none>' : expectedMarker}',
    );
    if (surfaceCount == 0 || (expectedMarker.isNotEmpty && !markerFound)) {
      exitCode = 2;
    }
  } catch (e, st) {
    stderr.writeln('FAIL $e');
    stderr.writeln(st);
    exitCode = 1;
  } finally {
    await channel.shutdown();
  }
}

void _printSurface(String prefix, Map<String, Object?> surface) {
  final actions = surface['actions'] is List
      ? surface['actions']! as List<Object?>
      : const <Object?>[];
  final hasSend = actions.any(
    (action) => action is Map && action['bindingId'] == 'ino.send',
  );
  final payload = surface['payload'];
  Object? operation;
  Object? nativeKind;
  if (payload is Map) {
    nativeKind = payload['nativeKind'];
    final data = payload['data'];
    if (data is Map) {
      operation = data['operation'];
    }
  }
  stdout.writeln(
    '$prefix id=${surface['surfaceId']} rev=${surface['revision']} '
    'seq=${surface['feedSequence']} nativeKind=$nativeKind '
    'hasInoSend=$hasSend actionCount=${actions.length}',
  );
  stdout.writeln('OPERATION ${jsonEncode(operation)}');
}
