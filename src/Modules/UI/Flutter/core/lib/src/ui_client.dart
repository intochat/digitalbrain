import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import 'basic_credentials.dart';
import 'agent_events.dart';
import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'sse_chat_frames.dart';
import 'sse_frames.dart';
import 'ui_models.dart';
import 'models/brain_models.dart';
import 'models/execution_activity.dart';

final class DigitalBrainUiClient {
  static const _uuid = Uuid();

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

  /// Sends only the new user message; the server owns conversation history.
  /// Canceling the subscription aborts both pending HTTP and response streaming.
  Stream<AgentEvent> runAgent({
    required String threadId,
    required String runId,
    String? parentRunId,
    required String text,
  }) {
    final abort = Completer<void>();
    StreamSubscription<AgentEvent>? incoming;
    late StreamController<AgentEvent> controller;
    controller = StreamController<AgentEvent>(
      onListen: () async {
        try {
          final request =
              http.AbortableRequest(
                  'POST',
                  baseUri.resolve('/agent'),
                  abortTrigger: abort.future,
                )
                ..headers.addAll({
                  'accept': 'text/event-stream',
                  'content-type': 'application/json',
                })
                ..body = jsonEncode({
                  'threadId': threadId,
                  'runId': runId,
                  'parentRunId': ?parentRunId,
                  'messages': [
                    {'id': _uuid.v4(), 'role': 'user', 'content': text},
                  ],
                  'tools': <Object>[],
                  'context': <Object>[],
                  'state': <String, Object>{},
                  'forwardedProps': <String, Object>{},
                });
          final response = await _http.send(request);
          if (abort.isCompleted) {
            await response.stream.listen(null).cancel();
            return;
          }
          if (response.statusCode != 200) {
            await response.stream.listen(null).cancel();
            throw StateError('Agent request failed (${response.statusCode}).');
          }
          incoming = decodeAgentEvents(response.stream).listen(
            controller.add,
            onError: (Object error, StackTrace stack) {
              if (!abort.isCompleted) controller.addError(error, stack);
            },
            onDone: controller.close,
            cancelOnError: false,
          );
        } catch (error, stack) {
          if (!abort.isCompleted) {
            controller.addError(error, stack);
            unawaited(controller.close());
          }
        }
      },
      onCancel: () async {
        if (!abort.isCompleted) abort.complete();
        await incoming?.cancel();
      },
    );
    return controller.stream;
  }

  Future<List<ChatTurnEvent>> readActivityResults({
    required String surfaceName,
    required String activityId,
  }) async {
    final response = await _request(
      'GET',
      '/surfaces/${Uri.encodeComponent(surfaceName)}/activities/${Uri.encodeComponent(activityId)}/results',
      timeout: const Duration(seconds: 15),
    );
    final decoded = jsonDecode(response.body);
    if (decoded is! List) {
      throw StateError('Activity results have an unsupported response.');
    }
    return decoded
        .whereType<Map>()
        .map((turn) => ChatTurnEvent.fromJson(Map<String, Object?>.from(turn)))
        .toList(growable: false);
  }

  Stream<List<ExecutionActivity>> watchActivities({String? surfaceName}) {
    final abort = Completer<void>();
    late StreamController<List<ExecutionActivity>> controller;
    controller = StreamController<List<ExecutionActivity>>(
      onListen: () async {
        final items = <String, ExecutionActivity>{};
        try {
          final request = http.AbortableRequest(
            'GET',
            baseUri.replace(
              path: surfaceName == null
                  ? '/activities/events'
                  : '/surfaces/${Uri.encodeComponent(surfaceName)}/activities/events',
            ),
            abortTrigger: abort.future,
          )..headers['accept'] = 'text/event-stream';
          final response = await _http.send(request);
          if (response.statusCode != 200) {
            throw StateError(
              'Activity stream unavailable (${response.statusCode}).',
            );
          }
          String? event;
          final data = <String>[];
          await for (final line
              in response.stream
                  .transform(utf8.decoder)
                  .transform(const LineSplitter())) {
            if (abort.isCompleted) break;
            if (line.startsWith('event:')) event = line.substring(6).trim();
            if (line.startsWith('data:')) {
              data.add(line.substring(5).trimLeft());
            }
            if (line.isEmpty) {
              if (data.isNotEmpty &&
                  (event == 'reset' || event == 'activity')) {
                final json =
                    jsonDecode(data.join('\n')) as Map<String, dynamic>;
                if (event == 'reset') {
                  items.clear();
                  for (final item
                      in ((json['state'] as Map)['activities'] as List? ??
                              const [])
                          .whereType<Map>()) {
                    final activity = ExecutionActivity.fromJson(
                      Map<String, dynamic>.from(item),
                    );
                    items[activity.id] = activity;
                  }
                } else {
                  final activity = ExecutionActivity.fromJson(json);
                  final current = items[activity.id];
                  if (current != null &&
                      current.version > 0 &&
                      activity.version <= current.version) {
                    event = null;
                    data.clear();
                    continue;
                  }
                  items[activity.id] = activity;
                }
                final sorted = items.values.toList()
                  ..sort((a, b) => b.startedAt.compareTo(a.startedAt));
                controller.add(List.unmodifiable(sorted));
              }
              event = null;
              data.clear();
            }
          }
        } catch (error, stack) {
          if (!abort.isCompleted) controller.addError(error, stack);
        } finally {
          if (!controller.isClosed) await controller.close();
        }
      },
      onCancel: () {
        if (!abort.isCompleted) abort.complete();
      },
    );
    return controller.stream;
  }

  Stream<BrainSnapshot> watchBrain({required String chatName}) {
    final abort = Completer<void>();
    late StreamController<BrainSnapshot> controller;
    controller = StreamController<BrainSnapshot>(
      onListen: () async {
        try {
          final request = http.AbortableRequest(
            'GET',
            baseUri.replace(
              path: '/chats/${Uri.encodeComponent(chatName)}/brain/events',
            ),
            abortTrigger: abort.future,
          )..headers['accept'] = 'text/event-stream';
          final response = await _http.send(request);
          if (response.statusCode != 200) {
            throw StateError(
              'Brain stream unavailable (${response.statusCode}).',
            );
          }
          String? event;
          final data = <String>[];
          await for (final line
              in response.stream
                  .transform(utf8.decoder)
                  .transform(const LineSplitter())) {
            if (abort.isCompleted) break;
            if (line.startsWith('event:')) event = line.substring(6).trim();
            if (line.startsWith('data:')) {
              data.add(line.substring(5).trimLeft());
            }
            if (line.isEmpty) {
              if (event == 'brain-snapshot' && data.isNotEmpty) {
                controller.add(
                  BrainSnapshot.fromJson(
                    jsonDecode(data.join('\n')) as Map<String, dynamic>,
                  ),
                );
              }
              event = null;
              data.clear();
            }
          }
        } catch (error, stack) {
          if (!abort.isCompleted) controller.addError(error, stack);
        } finally {
          if (!controller.isClosed) await controller.close();
        }
      },
      onCancel: () {
        if (!abort.isCompleted) abort.complete();
      },
    );
    return controller.stream;
  }

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
        ? http.Request(method, baseUri.resolve(path))
        : http.AbortableRequest(
            method,
            baseUri.resolve(path),
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

  Future<void> openSurface({
    required String surfaceName,
    required String surfaceKey,
    required String title,
  }) async {
    await _request(
      'POST',
      '/surfaces/${Uri.encodeComponent(surfaceName)}/open',
      body: {'surfaceKey': surfaceKey, 'title': title},
    );
  }

  Future<void> activateControl({
    required String surfaceName,
    required String surfaceKey,
    required String controlId,
    required String intent,
  }) async {
    await _request(
      'POST',
      '/surfaces/${Uri.encodeComponent(surfaceName)}/controls/'
          '${Uri.encodeComponent(controlId)}/activate',
      body: ActivateControlRequest(
        intent: intent,
        surfaceKey: surfaceKey,
      ).toJson(),
    );
  }

  Future<void> cancelTurn({
    required String chatName,
    required String turnId,
  }) async {
    await _request(
      'POST',
      '/chats/${Uri.encodeComponent(chatName)}/turns/${Uri.encodeComponent(turnId)}/cancel',
    );
  }

  Stream<SurfaceStreamEvent> watchSurfaceEvents({
    required String surfaceName,
    int afterSequence = 0,
  }) => _watchEvents<SurfaceStreamEvent>(
    path: '/surfaces/${Uri.encodeComponent(surfaceName)}/events',
    afterSequence: afterSequence,
    newParser: SseSurfaceEventParser.new,
    resumeCursor: (event) => switch (event) {
      SurfaceSignalObserved(:final sequence) => sequence,
      SurfaceStreamReset(:final cursor) => cursor,
    },
    failureLabel: 'surface events',
  );

  Stream<T> _watchEvents<T>({
    required String path,
    required int afterSequence,
    required SseFrameParser<T> Function() newParser,
    required int? Function(T event) resumeCursor,
    required String failureLabel,
  }) {
    var cursor = afterSequence;
    var cancelled = false;
    var retryMilliseconds = 500;
    Timer? retry;
    Completer<void>? requestAbort;
    StreamSubscription<String>? incoming;
    DateTime? connectedAt;
    late StreamController<T> controller;
    late Future<void> Function() connect;

    void emit(T event) {
      if (cancelled) return;
      final nextCursor = resumeCursor(event);
      if (nextCursor != null) cursor = nextCursor;
      controller.add(event);
    }

    void reconnect() {
      if (cancelled || retry != null) return;
      if (requestAbort case final abort? when !abort.isCompleted) {
        abort.complete();
      }
      if (connectedAt case final started?
          when DateTime.now().difference(started) >=
              const Duration(seconds: 30)) {
        retryMilliseconds = 500;
      }
      connectedAt = null;
      retry = Timer(Duration(milliseconds: retryMilliseconds), () {
        retry = null;
        unawaited(connect());
      });
      retryMilliseconds = (retryMilliseconds * 2).clamp(500, 8000);
    }

    connect = () async {
      if (cancelled) return;
      final abort = Completer<void>();
      requestAbort = abort;
      try {
        final request = http.AbortableRequest(
          'GET',
          baseUri.replace(
            path: path,
            queryParameters: {'afterSequence': '$cursor'},
          ),
          abortTrigger: abort.future,
        )..headers['accept'] = 'text/event-stream';
        final response = await _http
            .send(request)
            .timeout(const Duration(seconds: 15));
        if (cancelled || response.statusCode != 200) {
          await response.stream.listen(null).cancel();
          if (cancelled) return;
          throw StateError('$failureLabel failed: ${response.statusCode}');
        }
        connectedAt = DateTime.now();
        final parser = newParser();
        incoming = response.stream
            .transform(utf8.decoder)
            .transform(const LineSplitter())
            .listen(
              (line) {
                for (final event in parser.addLine(line)) {
                  emit(event);
                }
              },
              onError: (Object error, StackTrace stack) {
                if (cancelled) return;
                controller.addError(error, stack);
                reconnect();
              },
              onDone: () {
                for (final event in parser.flush()) {
                  emit(event);
                }
                reconnect();
              },
              cancelOnError: true,
            );
      } catch (error, stack) {
        if (cancelled) return;
        controller.addError(error, stack);
        reconnect();
      }
    };
    controller = StreamController<T>(
      onListen: () => unawaited(connect()),
      onCancel: () async {
        cancelled = true;
        retry?.cancel();
        if (requestAbort case final abort? when !abort.isCompleted) {
          abort.complete();
        }
        await incoming?.cancel();
      },
    );
    return controller.stream;
  }

  Future<ChatSendReceipt> sendMessage({
    required String chatName,
    required String text,
  }) async {
    final path = '/chats/${Uri.encodeComponent(chatName)}/send';
    final commandId = _uuid.v4();
    final response = await _request(
      'POST',
      path,
      body: {'commandId': commandId, 'text': text},
    );
    return ChatSendReceipt(
      turnId: _acceptedWorkId(response.body, path),
      commandId: commandId,
    );
  }

  Future<ChatSendReceipt> sendVoice({
    required String chatName,
    required List<int> audioBytes,
    String fileName = 'voice.wav',
  }) async {
    if (audioBytes.isEmpty) {
      throw StateError('voice upload requires non-empty audio');
    }

    final path = '/chats/${Uri.encodeComponent(chatName)}/voice';
    final request = http.MultipartRequest('POST', baseUri.replace(path: path))
      ..files.add(
        http.MultipartFile.fromBytes('audio', audioBytes, filename: fileName),
      );
    final response = await http.Response.fromStream(await _http.send(request));
    if (response.statusCode == 503) {
      throw StateError('voice unavailable: ${response.body}');
    }
    if (response.statusCode == 422) {
      throw StateError('transcription failed: ${response.body}');
    }
    if (response.statusCode != 202) {
      throw StateError(
        'chat.voice failed: ${response.statusCode} ${response.body}',
      );
    }
    // Voice mints its command id server-side and does not return it.
    return ChatSendReceipt(turnId: _acceptedWorkId(response.body, path));
  }

  String _acceptedWorkId(String body, String path) {
    final missingWork = 'POST $path response is missing work.value';
    Object? decoded;
    try {
      decoded = jsonDecode(body);
    } on FormatException {
      throw StateError(missingWork);
    }
    final work = decoded is Map ? decoded['work'] : null;
    final value = work is Map ? work['value'] : null;
    if (value is! String || value.isEmpty) {
      throw StateError(missingWork);
    }
    return value;
  }

  Stream<ChatStreamEvent> watchChatTurns({
    required String chatName,
    int afterSequence = 0,
  }) => _watchEvents<ChatStreamEvent>(
    path: '/chats/${Uri.encodeComponent(chatName)}/events',
    afterSequence: afterSequence,
    newParser: SseChatTurnParser.new,
    resumeCursor: (event) => switch (event) {
      ChatJournalReset(:final cursor) => cursor,
      ChatTurnObserved(:final turn) => turn.sequence > 0 ? turn.sequence : null,
    },
    failureLabel: 'chat events',
  );

  Future<ChatChartOffer?> readChart(String chartName) async {
    final body = await _getKitEntity('/kit/charts/$chartName', 'kit chart');
    return body == null ? null : ChatChartOffer.fromJson(body);
  }

  Future<ChatGraphOffer?> readGraph(String graphName) async {
    final body = await _getKitEntity('/kit/graphs/$graphName', 'kit graph');
    return body == null ? null : ChatGraphOffer.fromJson(body);
  }

  Future<KitSurfaceState?> readSurface(String surfaceName) async {
    final body = await _getKitEntity(
      '/kit/surfaces/$surfaceName',
      'kit surface',
    );
    return body == null ? null : KitSurfaceState.fromJson(body);
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
