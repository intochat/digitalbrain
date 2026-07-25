import 'dart:convert';
import 'dart:io' show Platform;

import 'package:digitalbrain_wire/digitalbrain_wire.dart';
import 'package:http/http.dart' as http;

import 'sse_frames.dart';

final class DigitalBrainUiEdgeClient {
  DigitalBrainUiEdgeClient({
    required this.baseUri,
    http.Client? httpClient,
  })  : _http = httpClient ?? http.Client(),
        _ownsClient = httpClient == null;

  factory DigitalBrainUiEdgeClient.fromEnvironment({http.Client? httpClient}) {
    const key = 'DIGITALBRAIN_UI_BASE';
    var raw = const String.fromEnvironment(key);
    if (raw.isEmpty) {
      raw = Platform.environment[key] ?? '';
    }
    if (raw.isEmpty) {
      throw StateError(
        'DIGITALBRAIN_UI_BASE is required (AppHost WithFlutterHost injects it).',
      );
    }
    return DigitalBrainUiEdgeClient(
      baseUri: Uri.parse(raw),
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

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
