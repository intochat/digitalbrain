import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:grpc/grpc.dart' as $grpc;

import 'web_grpc_channel_stub.dart' if (dart.library.js) 'web_grpc_channel.dart' as web_grpc;

import '../grpc/surfaces.pb.dart';
import '../grpc/surfaces.pbgrpc.dart';

/// Manages a real gRPC connection to the kernel's SurfaceStream service.
/// 
/// - subscribeSurfaces gives you the live UiSurfaceMessages (widget_json is the UiWidget tree).
/// - sendClientTap is the proper back-channel: when the user taps a Button rendered from the UI kit,
///   we send the OnTap synapse JSON back. On the server this becomes a ClientTap that DigitalBrainGrain
///   turns into the real synapse (InstallBundle, DismissAlarm, etc.) being delivered into the brain.
/// 
/// This is the Flutter equivalent of the hex1b client doing brain.SendAsync(onTap).
class SurfaceStreamConnection {
  dynamic _channel;
  SurfaceStreamClient? _client;
  StreamSubscription<UiSurfaceMessage>? _sub;

  final _messageController = StreamController<UiSurfaceMessage>.broadcast();
  Stream<UiSurfaceMessage> get messages => _messageController.stream;

  bool get isConnected => _channel != null;

  Future<void> connect(String host, int port, String username, {String brainId = 'main'}) async {
    await disconnect();

    if (kIsWeb) {
      _channel = web_grpc.createWebChannel(host, port);
    } else {
      _channel = $grpc.ClientChannel(
        host,
        port: port,
        options: $grpc.ChannelOptions(
          credentials: $grpc.ChannelCredentials.insecure(),
          idleTimeout: Duration(minutes: 60),
        ),
      );
    }

    _client = SurfaceStreamClient(_channel!);

    final request = SurfaceSubscription()
      ..username = username
      ..brainId = brainId;
    final stream = _client!.subscribeSurfaces(
      request,
      options: $grpc.CallOptions(metadata: {'username': username}),
    );

    _sub = stream.listen(
      (msg) => _messageController.add(msg),
      onError: (e) => _messageController.addError(e),
      onDone: () => _messageController.close(),
      cancelOnError: false,
    );
  }

  Future<void> sendClientTap(String surfaceId, Map<String, dynamic> onTapJson, String username) async {
    if (_client == null) return;

    final payload = jsonEncode(onTapJson);
    final event = ClientEvent()
      ..surfaceId = surfaceId
      ..eventType = 'tap'
      ..payloadJson = payload;

    try {
      await _client!.sendClientEvent(
        event,
        options: $grpc.CallOptions(metadata: {'username': username}),
      );
    } catch (e) {
      rethrow;
    }
  }

  Future<void> disconnect() async {
    await _sub?.cancel();
    _sub = null;
    final ch = _channel;
    if (ch is $grpc.ClientChannel) {
      await ch.shutdown();
    }
    _channel = null;
    _client = null;
  }

  Future<LoginResponse> login(String username) async {
    if (_client == null) throw StateError('not connected');
    final req = LoginRequest()..username = username;
    return _client!.login(req, options: $grpc.CallOptions(metadata: {'username': username}));
  }

  Future<BrainDescriptor> addBrain(String username, String brainName) async {
    if (_client == null) throw StateError('not connected');
    final req = AddBrainRequest()
      ..username = username
      ..brainName = brainName;
    return _client!.addBrain(req, options: $grpc.CallOptions(metadata: {'username': username}));
  }

  Future<ClientEventResponse> archiveBrain(String username, String brainName) async {
    if (_client == null) throw StateError('not connected');
    final req = ArchiveBrainRequest()
      ..username = username
      ..brainName = brainName;
    return _client!.archiveBrain(req, options: $grpc.CallOptions(metadata: {'username': username}));
  }
}