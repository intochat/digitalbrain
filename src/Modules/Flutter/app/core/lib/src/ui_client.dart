import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import 'agent_events.dart';
import 'basic_credentials.dart';
import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'models/brain_models.dart';
import 'models/table_models.dart';

final class DigitalBrainUiClient {
  static const _uuid = Uuid();

  /// Gated on the kernel; 404 when the kernel runs ungated.
  static const authCheckPath = '/auth/check';

  DigitalBrainUiClient({
    required this.baseUri,
    http.Client? httpClient,
    BasicCredentials? credentials,
  }) : workspaceIdentity = credentials?.username ?? 'local-owner',
       _http = httpClient is CookieHttpClient
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

  /// Non-secret scope for local workspace preferences; never includes credentials.
  final String workspaceIdentity;
  final CookieHttpClient _http;
  final bool _ownsClient;

  /// Transcription only: callers review the draft before starting an agent run.
  Future<String> transcribeVoice({
    required List<int> audioBytes,
    String fileName = 'voice.wav',
  }) async {
    if (audioBytes.isEmpty) throw ArgumentError('Audio is empty.');
    final request =
        http.MultipartRequest(
            'POST',
            baseUri.replace(path: '/agent/transcribe'),
          )
          ..files.add(
            http.MultipartFile.fromBytes(
              'audio',
              audioBytes,
              filename: fileName,
            ),
          );
    final response = await http.Response.fromStream(await _http.send(request));
    if (response.statusCode != 200) {
      throw StateError('Voice transcription unavailable: ${response.body}');
    }
    final body = jsonDecode(response.body) as Map;
    final text = body['text'];
    if (text is! String || text.trim().isEmpty) {
      throw StateError('No speech was recognized.');
    }
    return text;
  }

  Future<bool> salesforceConnected() async =>
      (await _tableRequest('GET', '/agent/connections/salesforce')
          as Map)['connected'] ==
      true;

  Future<List<Map<String, dynamic>>> listWorkspaceArtifacts() async =>
      (await _tableRequest('GET', '/workspace/artifacts') as List)
          .map((item) => Map<String, dynamic>.from(item as Map))
          .toList();

  Future<Map<String, dynamic>> readWorkspaceArtifact(String id) async =>
      Map<String, dynamic>.from(
        await _tableRequest(
          'GET',
          '/workspace/artifacts/${Uri.encodeComponent(id)}',
        ) as Map,
      );

  Future<Map<String, dynamic>> createWorkspaceArtifact({
    required String kind,
    required String title,
    required Map<String, dynamic> content,
  }) async => Map<String, dynamic>.from(
    await _tableRequest(
      'POST',
      '/workspace/artifacts',
      body: {'kind': kind, 'title': title, 'content': content},
    ) as Map,
  );

  Future<Map<String, dynamic>> updateWorkspaceArtifact(
    String id, {
    required int expectedRevision,
    required String title,
    required Map<String, dynamic> content,
  }) async => Map<String, dynamic>.from(
    await _tableRequest(
      'PUT',
      '/workspace/artifacts/${Uri.encodeComponent(id)}',
      body: {
        'expectedRevision': expectedRevision,
        'title': title,
        'content': content,
      },
    ) as Map,
  );

  Future<List<TableSummary>> listTables() async {
    final body = await _tableRequest('GET', '/ui/tables');
    return (body as List)
        .map(
          (item) =>
              TableSummary.fromJson(Map<String, dynamic>.from(item as Map)),
        )
        .toList();
  }

  Future<TableSnapshot> readTable(
    String id, {
    int offset = 0,
    int limit = 50,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'GET',
        '/ui/tables/${Uri.encodeComponent(id)}?offset=$offset&limit=$limit',
      ) as Map,
    ),
  );

  Future<TableSnapshot> createTable({
    required String title,
    required List<TableColumn> columns,
    required List<TableRowData> rows,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/ui/tables',
        body: {
          'title': title,
          'columns': columns.map((x) => x.toJson()).toList(),
          'rows': rows.map((x) => x.toJson()).toList(),
        },
      ) as Map,
    ),
  );

  Future<TableSnapshot> updateTableView(
    String id,
    TableViewUpdate update,
  ) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'PUT',
        '/ui/tables/${Uri.encodeComponent(id)}/view',
        body: update.toJson(),
      ) as Map,
    ),
  );

  Future<Object?> _tableRequest(
    String method,
    String path, {
    Map<String, Object?>? body,
  }) async {
    final abort = Completer<void>();
    final request = http.AbortableRequest(
      method,
      baseUri.resolve(path),
      abortTrigger: abort.future,
    );
    if (body != null) {
      request.headers['content-type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    final response = await _http
        .send(request)
        .then(http.Response.fromStream)
        .timeout(
          const Duration(seconds: 40),
          onTimeout: () {
            abort.complete();
            throw TimeoutException('Table request timed out.');
          },
        );
    if (response.statusCode < 200 || response.statusCode >= 300) {
      var message = response.body;
      try {
        final error = jsonDecode(message);
        if (error is Map) {
          final detail = error['error'] ?? error['detail'];
          if (detail is String) message = detail;
        }
      } on FormatException {
        // Non-JSON proxy/server failures still carry their original text.
      }
      throw TableRequestException(response.statusCode, message);
    }
    return jsonDecode(response.body);
  }

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

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
