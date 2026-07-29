import 'dart:async';
import 'dart:convert';

import 'ui_models.dart';
import 'package:http/http.dart' as http;

import 'host_environment.dart';
import 'sse_chat_delta_frames.dart';
import 'sse_chat_frames.dart';
import 'sse_frames.dart';

final class DigitalBrainUiClient {
  DigitalBrainUiClient({required this.baseUri, http.Client? httpClient})
    : _http = httpClient ?? http.Client(),
      _ownsClient = httpClient == null;

  factory DigitalBrainUiClient.fromEnvironment({
    http.Client? httpClient,
    Map<String, String>? processEnvironment,
  }) {
    return DigitalBrainUiClient(
      baseUri: DigitalBrainHostEnv.requireUiBaseUri(
        processEnvironment: processEnvironment,
      ),
      httpClient: httpClient,
    );
  }

  final Uri baseUri;
  final http.Client _http;
  final bool _ownsClient;

  Future<void> openScene({
    required String shellName,
    required String sceneKey,
    required String title,
  }) async {
    final uri = baseUri.replace(path: '/shells/$shellName/scenes');
    final response = await _http.post(
      uri,
      headers: {'content-type': 'application/json'},
      body: jsonEncode(
        OpenSceneRequest(sceneKey: sceneKey, title: title).toJson(),
      ),
    );
    if (response.statusCode != 202) {
      throw StateError(
        'open-scene failed: ${response.statusCode} ${response.body}',
      );
    }
  }

  Future<void> activateControl({
    required String sceneName,
    required String controlId,
    required String intent,
    String? sceneKey,
  }) async {
    final uri = baseUri.replace(
      path: '/scenes/$sceneName/controls/$controlId/activate',
    );
    final response = await _http.post(
      uri,
      headers: {'content-type': 'application/json'},
      body: jsonEncode(
        ActivateControlRequest(intent: intent, sceneKey: sceneKey).toJson(),
      ),
    );
    if (response.statusCode != 202) {
      throw StateError(
        'activate-control failed: ${response.statusCode} ${response.body}',
      );
    }
  }

  Stream<SceneOpenedEvent> watchShellEvents({
    required String shellName,
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/shells/$shellName/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final request = http.Request('GET', uri);
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      throw StateError('shell events failed: ${response.statusCode}');
    }

    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseSceneOpenedParser();
    await for (final line in lines) {
      for (final event in parser.addLine(line)) {
        yield event;
      }
    }
    for (final event in parser.flush()) {
      yield event;
    }
  }

  Stream<ChatDelta> streamMessage({
    required String chatName,
    required String text,
  }) async* {
    final uri = baseUri.replace(path: '/chats/$chatName/messages/stream');
    final request = http.Request('POST', uri)
      ..headers['content-type'] = 'application/json'
      ..body = jsonEncode(SendMessageRequest(text: text).toJson());
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      throw StateError(
        'stream-message failed: ${response.statusCode}',
      );
    }

    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseChatDeltaParser();
    await for (final line in lines) {
      for (final delta in parser.addLine(line)) {
        yield delta;
      }
    }
    for (final delta in parser.flush()) {
      yield delta;
    }
  }

  Stream<ChatTurnEvent> watchChatTurns({
    required String chatName,
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/chats/$chatName/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final request = http.Request('GET', uri);
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      throw StateError('chat events failed: ${response.statusCode}');
    }

    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseChatTurnParser();
    await for (final line in lines) {
      for (final event in parser.addLine(line)) {
        yield event;
      }
    }
    for (final event in parser.flush()) {
      yield event;
    }
  }

  Future<BrainTopologySnapshot> readBrainTopology({
    Duration requestTimeout = const Duration(seconds: 5),
  }) async {
    final uri = baseUri.replace(path: '/brain/topology');
    final abort = Completer<void>();
    final operation = () async {
      final request = http.AbortableRequest(
        'GET',
        uri,
        abortTrigger: abort.future,
      );
      final streamed = await _http.send(request);
      return http.Response.fromStream(streamed);
    }();
    final response = await operation.timeout(
      requestTimeout,
      onTimeout: () {
        abort.complete();
        throw http.RequestAbortedException(uri);
      },
    );
    if (response.statusCode != 200) {
      throw StateError(
        'brain-topology failed: ${response.statusCode} ${response.body}',
      );
    }

    final decoded = jsonDecode(response.body);
    if (decoded is! Map) {
      throw const FormatException('brain-topology response is not an object');
    }

    return BrainTopologySnapshot.fromJson(Map<String, Object?>.from(decoded));
  }

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
