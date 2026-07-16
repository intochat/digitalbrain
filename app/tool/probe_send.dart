import 'dart:convert';
import 'dart:io';
import 'package:digitalbrain_flutter/runtime/grpc_ui_transport.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/feed_state.dart';

Future<void> main() async {
  final endpoint = Uri.parse('https://localhost:58997');
  final transport = GrpcUiTransport.connect(endpoint);
  try {
    final session = await transport.login(username: 'admin', password: 'admin');
    stdout.writeln('session ok actor=${session.identity.actorId} owner=${session.identity.ownerId}');
    final caps = const ClientCapabilities().names;
    final call = await transport.watchSurfaceFeed(
      accessToken: session.credentials.accessToken,
      afterSequence: 0,
      audience: FeedAudience.actor,
      clientCapabilities: caps,
      maxBatchSize: 50,
    );
    await for (final event in call.events.timeout(const Duration(seconds: 8))) {
      if (event is FeedSurfaceJson) {
        final map = jsonDecode(event.surfaceJson) as Map<String, Object?>;
        final actions = map['actions'];
        final payload = map['payload'];
        stdout.writeln('SURFACE id=${map['surfaceId']} rev=${map['revision']} seq=${map['feedSequence']}');
        stdout.writeln('actions=${jsonEncode(actions)}');
        if (payload is Map) {
          final data = payload['data'];
          final op = data is Map ? data['operation'] : null;
          stdout.writeln('nativeKind=${payload['nativeKind']} operation=${jsonEncode(op)}');
        }
        final actionList = actions is List ? actions : const [];
        final hasSend = actionList.any((a) => a is Map && a['bindingId'] == 'ino.send');
        stdout.writeln('HAS_INO_SEND=$hasSend actionCount=${actionList.length}');
        await call.cancel();
        break;
      } else if (event is FeedResetEvent) {
        stdout.writeln('RESET reason=${event.reason} resume=${event.resumeSequence} snaps=${event.snapshotJson.length}');
        for (final snap in event.snapshotJson) {
          final map = jsonDecode(snap) as Map<String, Object?>;
          final actions = map['actions'] as List? ?? const [];
          final hasSend = actions.any((a) => a is Map && a['bindingId'] == 'ino.send');
          stdout.writeln('SNAP id=${map['surfaceId']} rev=${map['revision']} HAS_INO_SEND=$hasSend actions=${jsonEncode(actions)}');
          final payload = map['payload'];
          if (payload is Map) {
            final data = payload['data'];
            final op = data is Map ? data['operation'] : null;
            stdout.writeln('  nativeKind=${payload['nativeKind']} operation=${jsonEncode(op)}');
          }
        }
        await call.cancel();
        break;
      }
    }
  } catch (e, st) {
    stderr.writeln('FAIL $e');
    stderr.writeln(st);
    exitCode = 1;
  } finally {
    await transport.close();
  }
}
