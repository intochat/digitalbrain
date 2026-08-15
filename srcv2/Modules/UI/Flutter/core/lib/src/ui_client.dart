import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'sse_authorization_frames.dart';
import 'sse_chat_delta_frames.dart';
import 'sse_chat_frames.dart';
import 'sse_frames.dart';
import 'sse_graph_frames.dart';
import 'ui_models.dart';

final class DigitalBrainUiClient {
  DigitalBrainUiClient({
    required this.baseUri,
    http.Client? httpClient,
    String? username,
    String? password,
  }) : username = username ?? DigitalBrainHostEnv.defaultUsername,
       password = password ?? DigitalBrainHostEnv.defaultPassword,
       _http = httpClient is CookieHttpClient
           ? httpClient
           : CookieHttpClient(httpClient ?? http.Client()),
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
      username: DigitalBrainHostEnv.resolveUsername(
        processEnvironment: processEnvironment,
      ),
      password: DigitalBrainHostEnv.resolvePassword(
        processEnvironment: processEnvironment,
      ),
    );
  }

  final Uri baseUri;
  final String username;
  final String password;
  final CookieHttpClient _http;
  final bool _ownsClient;

  /// Shared cookie jar for [BehaviorClient.sharingSession] and other edge clients.
  CookieHttpClient get cookieClient => _http;

  Future<AuthMe>? _session;
  bool _sessionReady = false;

  /// Establish an authenticated principal before product streams/commands.
  /// Uses loopback bootstrap owner when present; otherwise bootstrap then login.
  Future<AuthMe> ensureSession() {
    final existing = _session;
    if (existing != null) {
      return existing;
    }

    final started = _establishSession();
    _session = started;
    return started;
  }

  Future<AuthMe> _establishSession() async {
    try {
      final me = await readMe();
      if (me != null) {
        _sessionReady = true;
        return me;
      }

      await _tryBootstrap();
      final afterBootstrap = await readMe();
      if (afterBootstrap != null) {
        _sessionReady = true;
        return afterBootstrap;
      }

      await login(username: username, password: password);
      final afterLogin = await readMe();
      if (afterLogin != null) {
        _sessionReady = true;
        return afterLogin;
      }

      throw StateError(
        'DigitalBrain auth failed: /auth/me still unauthorized after '
        'bootstrap/login as "$username". Check kernel identity tables and '
        'loopback/cookie path.',
      );
    } catch (error) {
      _session = null;
      _sessionReady = false;
      rethrow;
    }
  }

  Future<AuthMe?> readMe() async {
    final uri = baseUri.replace(path: '/auth/me');
    final response = await _http.get(uri);
    if (response.statusCode == 401 || response.statusCode == 403) {
      return null;
    }
    if (response.statusCode != 200) {
      throw StateError('auth/me failed: ${response.statusCode} ${response.body}');
    }

    final decoded = jsonDecode(response.body);
    if (decoded is! Map) {
      throw const FormatException('auth/me response is not an object');
    }
    return AuthMe.fromJson(Map<String, Object?>.from(decoded));
  }

  Future<void> _tryBootstrap() async {
    final uri = baseUri.replace(path: '/auth/bootstrap');
    final response = await _http.post(
      uri,
      headers: {'content-type': 'application/json'},
      body: jsonEncode({'username': username, 'password': password}),
    );
    // 200 created, 409 already exists — either is fine for ensureSession.
    if (response.statusCode == 200 || response.statusCode == 409) {
      return;
    }
    if (response.statusCode == 400) {
      // Password policy / validation — fall through to login.
      return;
    }
    throw StateError(
      'auth/bootstrap failed: ${response.statusCode} ${response.body}',
    );
  }

  Future<AuthMe> login({
    required String username,
    required String password,
  }) async {
    final uri = baseUri.replace(path: '/auth/login');
    final response = await _http.post(
      uri,
      headers: {'content-type': 'application/json'},
      body: jsonEncode({'username': username, 'password': password}),
    );
    if (response.statusCode != 200) {
      throw StateError(
        'auth/login failed: ${response.statusCode} ${response.body}',
      );
    }

    final decoded = jsonDecode(response.body);
    if (decoded is! Map) {
      throw const FormatException('auth/login response is not an object');
    }
    final me = AuthMe.fromJson(Map<String, Object?>.from(decoded));
    _sessionReady = true;
    _session = Future.value(me);
    return me;
  }

  Future<void> _requireSession() async {
    if (_sessionReady) {
      return;
    }
    await ensureSession();
  }

  Future<http.StreamedResponse> _sendAuthed(http.BaseRequest request) async {
    await _requireSession();
    var response = await _http.send(request);
    if (response.statusCode == 401 || response.statusCode == 403) {
      // Session lost or seed raced; clear and retry once.
      _session = null;
      _sessionReady = false;
      await ensureSession();
      final retry = http.Request(request.method, request.url);
      request.headers.forEach((key, value) {
        retry.headers.putIfAbsent(key, () => value);
      });
      if (request is http.Request && request.bodyBytes.isNotEmpty) {
        retry.bodyBytes = request.bodyBytes;
      }
      response = await _http.send(retry);
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
    final streamed = await _sendAuthed(request);
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 202) {
      throw StateError(
        'surface.open failed: ${response.statusCode} ${response.body}',
      );
    }
  }

  Future<void> activateChatButton({
    required String chatName,
    required String offerCommandId,
    required String buttonId,
    required String action,
  }) async {
    final uri = baseUri.replace(path: '/owner/commands');
    final request = http.Request('POST', uri)
      ..headers['content-type'] = 'application/json'
      ..body = jsonEncode({
        'kind': 'chat.button',
        'chatName': chatName,
        'offerCommandId': offerCommandId,
        'buttonId': buttonId,
        'action': action,
      });
    final streamed = await _sendAuthed(request);
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 202) {
      throw StateError(
        'chat.button failed: ${response.statusCode} ${response.body}',
      );
    }
  }

  Stream<SceneOpenedEvent> watchShellEvents({
    required String shellName,
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/surfaces/$shellName/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final response = await _sendAuthed(http.Request('GET', uri));
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
    final response = await _sendAuthed(request);
    if (response.statusCode != 200) {
      throw StateError('chat.send failed: ${response.statusCode}');
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

    await _requireSession();
    final uri = baseUri.replace(path: '/chats/$chatName/voice');

    Future<http.StreamedResponse> postOnce() {
      final request = http.MultipartRequest('POST', uri)
        ..files.add(
          http.MultipartFile.fromBytes(
            'audio',
            audioBytes,
            filename: fileName,
          ),
        );
      return _http.send(request);
    }

    var response = await postOnce();
    if (response.statusCode == 401 || response.statusCode == 403) {
      _session = null;
      _sessionReady = false;
      await ensureSession();
      response = await postOnce();
    }

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
      throw StateError(
        'chat.voice failed: ${response.statusCode} $body',
      );
    }

    yield* _parseChatDeltas(response);
  }

  Stream<ChatDelta> _parseChatDeltas(http.StreamedResponse response) async* {
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
    final response = await _sendAuthed(http.Request('GET', uri));
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

  Stream<GraphChangeEvent> watchGraphChanges({
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/graph/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final response = await _sendAuthed(http.Request('GET', uri));
    if (response.statusCode != 200) {
      throw StateError('graph events failed: ${response.statusCode}');
    }

    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseGraphChangeParser();
    await for (final line in lines) {
      for (final event in parser.addLine(line)) {
        yield event;
      }
    }
    for (final event in parser.flush()) {
      yield event;
    }
  }

  Stream<AuthorizationEvent> watchAuthorizations({
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/authorizations/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final response = await _sendAuthed(http.Request('GET', uri));
    if (response.statusCode != 200) {
      throw StateError('authorization events failed: ${response.statusCode}');
    }

    final lines = response.stream
        .transform(utf8.decoder)
        .transform(const LineSplitter());

    final parser = SseAuthorizationParser();
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
    await _requireSession();
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
    if (response.statusCode == 401 || response.statusCode == 403) {
      _session = null;
      _sessionReady = false;
      await ensureSession();
      final retry = await _http.get(uri);
      if (retry.statusCode != 200) {
        throw StateError(
          'brain-topology failed: ${retry.statusCode} ${retry.body}',
        );
      }
      final decodedRetry = jsonDecode(retry.body);
      if (decodedRetry is! Map) {
        throw const FormatException('brain-topology response is not an object');
      }
      return BrainTopologySnapshot.fromJson(
        Map<String, Object?>.from(decodedRetry),
      );
    }
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

final class AuthMe {
  const AuthMe({
    required this.username,
    required this.principalId,
    required this.isBootstrapOwner,
  });

  final String username;
  final String principalId;
  final bool isBootstrapOwner;

  factory AuthMe.fromJson(Map<String, Object?> json) {
    return AuthMe(
      username: json['username'] as String? ?? '',
      principalId: json['principalId'] as String? ?? '',
      isBootstrapOwner: json['isBootstrapOwner'] as bool? ?? false,
    );
  }
}
