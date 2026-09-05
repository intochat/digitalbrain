import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import 'basic_credentials.dart';
import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'sse_chat_delta_frames.dart';
import 'sse_chat_frames.dart';
import 'sse_frames.dart';
import 'ui_models.dart';
import 'models/brain_models.dart';

final class DigitalBrainUiClient {
  /// Gated on the kernel; 404 when the kernel runs ungated.
  static const authCheckPath = '/auth/check';

  DigitalBrainUiClient({
    required this.baseUri,
    http.Client? httpClient,
    BasicCredentials? credentials,
  }) : _http = httpClient is CookieHttpClient
           ? httpClient
           : CookieHttpClient(
               httpClient ?? http.Client(),
               credentials: credentials,
             ),
       _ownsClient = httpClient == null;

  factory DigitalBrainUiClient.fromEnvironment({
    http.Client? httpClient,
    Map<String, String>? processEnvironment,
    BasicCredentials? credentials,
  }) {
    return DigitalBrainUiClient(
      baseUri: DigitalBrainHostEnv.requireUiBaseUri(
        processEnvironment: processEnvironment,
      ),
      httpClient: httpClient,
      credentials: credentials,
    );
  }

  /// Probes the kernel's gated `/auth/check`.
  ///
  /// Returns true when the kernel accepts the attached credentials — including
  /// the ungated case, where the endpoint is absent and any request already
  /// succeeds. Returns false only on an explicit 401, so a wrong password is
  /// distinguishable from an unreachable kernel, which throws.
  Future<bool> checkAuth() async {
    final uri = baseUri.replace(path: authCheckPath);
    final streamed = await _http.send(http.Request('GET', uri));
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 401) {
      return false;
    }
    if (response.statusCode == 204 || response.statusCode == 404) {
      return true;
    }
    throw StateError(
      'auth check failed: ${response.statusCode} ${response.body}',
    );
  }

  final Uri baseUri;
  final CookieHttpClient _http;
  final bool _ownsClient;

  Future<BrainSnapshot> readBrain({required String chatName}) async {
    final response = await _request(
      'GET',
      '/chats/${Uri.encodeComponent(chatName)}/brain',
      timeout: const Duration(seconds: 10),
    );
    return BrainSnapshot.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Future<void> setBrainSubscription({
    required String chatName,
    required String sourceId,
    required String targetId,
    required String signalType,
    required bool subscribed,
  }) async {
    await _request(
      'POST',
      '/chats/${Uri.encodeComponent(chatName)}/brain/subscriptions',
      timeout: const Duration(seconds: 14),
      body: {
        'sourceId': sourceId,
        'targetId': targetId,
        'signalType': signalType,
        'subscribed': subscribed,
      },
    );
  }

  Future<http.Response> _request(
    String method,
    String path, {
    Map<String, Object?>? body,
    Duration? timeout,
  }) async {
    final abort = timeout == null ? null : Completer<void>();
    final request = abort == null
        ? http.Request(method, baseUri.replace(path: path))
        : http.AbortableRequest(
            method,
            baseUri.replace(path: path),
            abortTrigger: abort.future,
          );
    if (body != null) {
      request.headers['content-type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    final operation = _http.send(request).then(http.Response.fromStream);
    final response = timeout == null
        ? await operation
        : await operation.timeout(
            timeout,
            onTimeout: () {
              // Stop the actual request, so timed-out graph polls cannot accumulate
              // network work while the store schedules a retry.
              abort!.complete();
              throw TimeoutException('$method $path timed out', timeout);
            },
          );
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw StateError(
        '$method $path failed: ${response.statusCode} ${response.body}',
      );
    }
    return response;
  }

  Future<void> openScene({
    required String shellName,
    required String sceneKey,
    required String title,
  }) async {
    final uri = baseUri.replace(path: '/owner/commands');
    final request = http.Request('POST', uri)
      ..headers['content-type'] = 'application/json'
      ..body = jsonEncode({
        'kind': 'surface.open',
        'surfaceName': shellName,
        'surfaceKey': sceneKey,
        'title': title,
      });
    final streamed = await _http.send(request);
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 202) {
      throw StateError(
        'surface.open failed: ${response.statusCode} ${response.body}',
      );
    }
  }

  Future<void> cancelTurn({
    required String chatName,
    required String commandId,
    required String turnId,
  }) async {
    await _request(
      'POST',
      '/owner/commands',
      body: {
        'kind': 'chat.cancel-turn',
        'chatName': chatName,
        'commandId': commandId,
        'turnId': turnId,
      },
    );
  }

  Stream<SceneOpenedEvent> watchShellEvents({
    required String shellName,
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/surfaces/$shellName/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final response = await _http.send(http.Request('GET', uri));
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
    final uri = baseUri.replace(path: '/owner/commands');
    final request = http.Request('POST', uri)
      ..headers['content-type'] = 'application/json'
      ..body = jsonEncode({
        'kind': 'chat.send',
        'chatName': chatName,
        'text': text,
      });
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      final body = await response.stream.bytesToString();
      throw StateError('chat.send failed: ${response.statusCode} $body');
    }

    yield* _parseChatDeltas(response);
  }

  // Multipart voice note → server Whisper → same durable chat turn SSE as streamMessage.
  Stream<ChatDelta> streamVoice({
    required String chatName,
    required List<int> audioBytes,
    String fileName = 'voice.wav',
  }) async* {
    if (audioBytes.isEmpty) {
      throw StateError('voice upload requires non-empty audio');
    }

    final uri = baseUri.replace(path: '/chats/$chatName/voice');

    Future<http.StreamedResponse> postOnce() {
      final request = http.MultipartRequest('POST', uri)
        ..files.add(
          http.MultipartFile.fromBytes('audio', audioBytes, filename: fileName),
        );
      return _http.send(request);
    }

    final response = await postOnce();

    if (response.statusCode == 503) {
      final body = await response.stream.bytesToString();
      throw StateError('voice unavailable: $body');
    }
    if (response.statusCode == 422) {
      final body = await response.stream.bytesToString();
      throw StateError('transcription failed: $body');
    }
    if (response.statusCode != 200) {
      final body = await response.stream.bytesToString();
      throw StateError('chat.voice failed: ${response.statusCode} $body');
    }

    yield* _parseChatDeltas(response);
  }

  Stream<ChatDelta> _parseChatDeltas(http.StreamedResponse response) async* {
    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseChatDeltaParser();
    var receivedResponse = false;
    await for (final line in lines) {
      for (final delta in parser.addLine(line)) {
        receivedResponse = receivedResponse || !delta.isAcceptance;
        yield delta;
      }
    }
    for (final delta in parser.flush()) {
      receivedResponse = receivedResponse || !delta.isAcceptance;
      yield delta;
    }
    if (!receivedResponse) {
      throw StateError(
        'The assistant connection ended without a response. Please try again.',
      );
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
    final response = await _http.send(http.Request('GET', uri));
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

  Future<ChatChartOffer?> readChart(String chartName) async {
    final body = await _getKitEntity('/kit/charts/$chartName', 'kit chart');
    return body == null ? null : ChatChartOffer.fromJson(body);
  }

  Future<ChatGraphOffer?> readGraph(String graphName) async {
    final body = await _getKitEntity('/kit/graphs/$graphName', 'kit graph');
    return body == null ? null : ChatGraphOffer.fromJson(body);
  }

  Future<ChatSpreadsheetOffer?> readSpreadsheet(String spreadsheetName) async {
    final body = await _getKitEntity(
      '/kit/spreadsheets/$spreadsheetName',
      'kit spreadsheet',
    );
    return body == null ? null : ChatSpreadsheetOffer.fromJson(body);
  }

  Future<Map<String, Object?>?> readImage(String imageName) =>
      _getKitEntity('/kit/images/$imageName', 'kit image');

  Future<Uint8List?> readImageBytes(String imageName) async {
    final uri = baseUri.replace(path: '/kit/images/$imageName/content');
    final streamed = await _http.send(http.Request('GET', uri));
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 404) {
      return null;
    }
    if (response.statusCode != 200) {
      throw StateError(
        'kit image content read failed: ${response.statusCode} ${response.body}',
      );
    }
    return response.bodyBytes;
  }

  Future<Map<String, Object?>?> _getKitEntity(
    String path,
    String description,
  ) async {
    final uri = baseUri.replace(path: path);
    final streamed = await _http.send(http.Request('GET', uri));
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 404) {
      return null;
    }
    if (response.statusCode != 200) {
      throw StateError(
        '$description read failed: ${response.statusCode} ${response.body}',
      );
    }
    return jsonDecode(response.body) as Map<String, Object?>;
  }

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
