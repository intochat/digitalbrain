import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';
import 'package:http/http.dart' as http;

/// Northbound HTTP client for hosts/DigitalBrain.Ui. No Orleans. No MCP tools.
final class DigitalBrainUiEdgeClient {
  DigitalBrainUiEdgeClient({
    required this.baseUri,
    http.Client? httpClient,
  })  : _http = httpClient ?? http.Client(),
        _ownsClient = httpClient == null;

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

  /// Parses SSE `data:` lines of event type scene-opened (or untyped JSON).
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

    String? dataLine;
    await for (final line in lines) {
      if (line.startsWith('data:')) {
        dataLine = line.substring('data:'.length).trim();
      } else if (line.isEmpty && dataLine != null) {
        final decoded = jsonDecode(dataLine);
        dataLine = null;
        if (decoded is Map<String, dynamic>) {
          yield SceneOpenedEvent.fromJson(decoded.cast<String, Object?>());
        }
      }
    }
  }

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
