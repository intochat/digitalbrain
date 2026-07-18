import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:web_socket_channel/web_socket_channel.dart';

import '../surface/ui_surface_client.dart';
import '../surface/ui_surface_models.dart';

class GatewayException implements Exception {
  GatewayException(this.code, this.detail);

  final String code;
  final String detail;

  @override
  String toString() => 'GatewayException($code, $detail)';
}

class BrainGateway implements UiSurfaceClient {
  BrainGateway({
    required this.httpBase,
    required this.wsBase,
    http.Client? client,
  }) : _client = client ?? http.Client();

  final String httpBase;
  final String wsBase;
  final http.Client _client;

  int lastSequence = 0;

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    final uri = Uri.parse('$httpBase/ui/surface').replace(
      queryParameters: {'surfaceId': surfaceId},
    );
    final response = await _client.get(uri);
    final body = _decodeBody(response);
    _requireSchemaVersion(body);
    return UiSurfaceSnapshot.fromJson(body);
  }

  @override
  Future<void> sendSurfaceAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  }) async {
    final response = await _client.post(
      Uri.parse('$httpBase/ui/action'),
      headers: const {'Content-Type': 'application/json'},
      body: jsonEncode({
        'surfaceId': surfaceId,
        'actionId': actionId,
        'expectedRevision': expectedRevision,
      }),
    );
    _decodeBody(response);
  }

  @override
  Stream<UiFeedMessage> watch({required int cursor}) {
    final controller = StreamController<UiFeedMessage>();
    WebSocketChannel? channel;
    StreamSubscription<dynamic>? subscription;

    Future<void> open() async {
      try {
        final uri = Uri.parse('$wsBase/ui/watch').replace(
          queryParameters: {'cursor': '$cursor'},
        );
        channel = WebSocketChannel.connect(uri);
        await channel!.ready;
        subscription = channel!.stream.listen(
          (message) {
            if (controller.isClosed) {
              return;
            }
            if (message is! String) {
              return;
            }
            try {
              final frame = mapFrame(message);
              if (frame == null) {
                return;
              }
              lastSequence = frame.sequence;
              controller.add(frame);
            } catch (error, stackTrace) {
              controller.addError(_sanitizeError(error), stackTrace);
            }
          },
          onError: (Object error, StackTrace stackTrace) {
            if (!controller.isClosed) {
              controller.addError(_sanitizeError(error), stackTrace);
            }
          },
          onDone: () {
            if (!controller.isClosed) {
              unawaited(controller.close());
            }
          },
          cancelOnError: false,
        );
      } catch (error, stackTrace) {
        if (!controller.isClosed) {
          controller.addError(_sanitizeError(error), stackTrace);
          unawaited(controller.close());
        }
      }
    }

    controller.onListen = () {
      unawaited(open());
    };
    controller.onCancel = () async {
      await subscription?.cancel();
      subscription = null;
      final active = channel;
      channel = null;
      if (active != null) {
        await active.sink.close().catchError((_) {});
      }
    };

    return controller.stream;
  }

  static UiFeedMessage? mapFrame(String text) {
    Map<String, dynamic> decoded;
    try {
      final value = jsonDecode(text);
      if (value is! Map<String, dynamic>) {
        throw GatewayException('frame.invalid', 'feed frame must be an object');
      }
      decoded = value;
    } on FormatException {
      throw GatewayException('frame.invalid', 'feed frame is not valid json');
    } on TypeError {
      throw GatewayException('frame.invalid', 'feed frame is not valid json');
    }

    if (decoded['ping'] == true) {
      return null;
    }

    final schemaVersion = decoded['schemaVersion'];
    if (schemaVersion is! int ||
        schemaVersion != UiFeedMessage.supportedSchemaVersion) {
      throw GatewayException(
        'schema.unsupported',
        'unsupported schema version',
      );
    }

    final sequence = decoded['sequence'];
    if (sequence is! int || sequence < 0) {
      throw GatewayException('sequence.invalid', 'invalid feed sequence');
    }

    try {
      return UiFeedMessage.parse(decoded);
    } on FormatException catch (error) {
      final message = error.message;
      if (message.contains('schema')) {
        throw GatewayException('schema.unsupported', 'unsupported schema version');
      }
      if (message.contains('sequence')) {
        throw GatewayException('sequence.invalid', 'invalid feed sequence');
      }
      throw GatewayException('frame.invalid', 'feed frame rejected');
    }
  }

  void _requireSchemaVersion(Map<String, dynamic> body) {
    final schemaVersion = body['schemaVersion'];
    if (schemaVersion is! int ||
        schemaVersion != UiFeedMessage.supportedSchemaVersion) {
      throw GatewayException(
        'schema.unsupported',
        'unsupported schema version',
      );
    }
  }

  Object _sanitizeError(Object error) {
    if (error is GatewayException) {
      return GatewayException(error.code, _sanitizedDetail(error.code));
    }
    return GatewayException('transport.error', 'connection failure');
  }

  static String _sanitizedDetail(String code) {
    switch (code) {
      case 'schema.unsupported':
        return 'unsupported schema version';
      case 'sequence.invalid':
        return 'invalid feed sequence';
      case 'frame.invalid':
        return 'feed frame rejected';
      default:
        return 'connection failure';
    }
  }

  Map<String, dynamic> _decodeBody(http.Response response) {
    if (response.statusCode == 409) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      throw GatewayException(
        body['code'] as String? ?? 'conflict',
        body['detail'] as String? ?? 'conflict',
      );
    }
    if (response.statusCode != 200) {
      throw GatewayException('http.error', 'status ${response.statusCode}');
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }
}
