import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import 'agent_events.dart';
import 'basic_credentials.dart';
import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'models/app_manifest.dart';
import 'models/brain_models.dart';
import 'models/inbox_models.dart';
import 'models/table_models.dart';
import 'models/workspace_models.dart';

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

  Future<bool> salesforceConnected() async =>
      (await _tableRequest('GET', '/agent/connections/salesforce')
          as Map)['connected'] ==
      true;

  Future<List<TableSummary>> listTables(String workspace) async {
    final body = await _tableRequest(
      'GET',
      '/workspaces/${Uri.encodeComponent(workspace)}/ui/tables',
    );
    return (body as List)
        .map(
          (item) =>
              TableSummary.fromJson(Map<String, dynamic>.from(item as Map)),
        )
        .toList();
  }

  /// The launcher reads installed manifests; the shell never hard-codes apps.
  Future<List<AppManifestSummary>> listApps(String workspace) async {
    final body = await _tableRequest(
      'GET',
      '/workspaces/${Uri.encodeComponent(workspace)}/apps',
    );
    return (body as List)
        .map(
          (item) => AppManifestSummary.fromJson(
            Map<String, dynamic>.from(item as Map),
          ),
        )
        .toList();
  }

  Future<TableSnapshot> readTable(
    String workspace,
    String id, {
    int offset = 0,
    int limit = 50,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'GET',
        '/workspaces/${Uri.encodeComponent(workspace)}/ui/tables/${Uri.encodeComponent(id)}?offset=$offset&limit=$limit',
      ) as Map,
    ),
  );

  Future<TableSnapshot> createTable(
    String workspace, {
    required String title,
    required List<TableColumn> columns,
    required List<TableRowData> rows,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(workspace)}/ui/tables',
        body: {
          'title': title,
          'columns': columns.map((x) => x.toJson()).toList(),
          'rows': rows.map((x) => x.toJson()).toList(),
        },
      ) as Map,
    ),
  );

  Future<TableSnapshot> updateTableView(
    String workspace,
    String id,
    TableViewUpdate update,
  ) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(workspace)}/ui/tables/${Uri.encodeComponent(id)}/view',
        body: update.toJson(),
      ) as Map,
    ),
  );

  Future<WorkspaceSnapshot> readWorkspace(
    String workspaceId, {
    Future<void>? cancelled,
  }) async => WorkspaceSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'GET',
        '/workspaces/${Uri.encodeComponent(workspaceId)}',
        cancelled: cancelled,
      ) as Map,
    ),
  );

  Future<WorkspaceSnapshot> closeWorkspaceWindow(
    String workspaceId,
    String windowId,
    int revision,
  ) async => WorkspaceSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(workspaceId)}/windows/${Uri.encodeComponent(windowId)}/close',
        body: {'expectedRevision': revision},
      ) as Map,
    ),
  );

  Future<WorkspaceSnapshot> reopenWorkspaceWindow(
    String workspaceId,
    String windowId,
    int revision,
  ) async => WorkspaceSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(workspaceId)}/windows/${Uri.encodeComponent(windowId)}/reopen',
        body: {'operationId': _uuid.v4(), 'expectedRevision': revision},
      ) as Map,
    ),
  );
  Future<void> reportProblem({
    required String workspaceId,
    required String intentId,
    required String message,
  }) async {
    await _tableRequest(
      'POST',
      '/workspaces/${Uri.encodeComponent(workspaceId)}/reports',
      body: {'intentId': intentId, 'message': message},
    );
  }

  Future<TableSnapshot> readWorkspaceTable(
    String workspaceId,
    String tableId, {
    int offset = 0,
    int limit = 25,
    Future<void>? cancelled,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'GET',
        '/workspaces/${Uri.encodeComponent(workspaceId)}/tables/${Uri.encodeComponent(tableId)}?offset=$offset&limit=$limit',
        cancelled: cancelled,
      ) as Map,
    ),
  );

  Future<TableSnapshot> updateWorkspaceTable(
    String workspaceId,
    String tableId,
    TableViewUpdate update, {
    Future<void>? cancelled,
  }) async => TableSnapshot.fromJson(
    Map<String, dynamic>.from(
      await _tableRequest(
        'POST',
        '/workspaces/${Uri.encodeComponent(workspaceId)}/tables/${Uri.encodeComponent(tableId)}/view',
        body: update.toJson(),
        cancelled: cancelled,
      ) as Map,
    ),
  );

  Stream<WorkspaceRevision> watchWorkspace(String workspaceId) {
    final abort = Completer<void>();
    StreamSubscription<String>? incoming;
    late StreamController<WorkspaceRevision> controller;
    controller = StreamController<WorkspaceRevision>(
      onListen: () async {
        try {
          final request = http.AbortableRequest(
            'GET',
            baseUri.resolve(
              '/workspaces/${Uri.encodeComponent(workspaceId)}/events',
            ),
            abortTrigger: abort.future,
          )..headers['accept'] = 'text/event-stream';
          final response = await _http.send(request);
          if (abort.isCompleted || response.statusCode != 200) {
            await response.stream.listen(null).cancel();
            if (!abort.isCompleted) {
              throw TableRequestException(
                response.statusCode,
                'Workspace events are unavailable.',
              );
            }
            return;
          }
          incoming = response.stream
              .transform(utf8.decoder)
              .transform(const LineSplitter())
              .listen(
                (line) {
                  if (!line.startsWith('data:')) return;
                  try {
                    controller.add(
                      WorkspaceRevision.fromJson(
                        jsonDecode(line.substring(5).trimLeft())
                            as Map<String, dynamic>,
                      ),
                    );
                  } catch (error, stack) {
                    controller.addError(error, stack);
                  }
                },
                onError: (Object error, StackTrace stack) {
                  if (!abort.isCompleted) controller.addError(error, stack);
                },
                onDone: controller.close,
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

  Future<Object?> _tableRequest(
    String method,
    String path, {
    Map<String, Object?>? body,
    Duration timeout = const Duration(seconds: 40),
    Future<void>? cancelled,
  }) async {
    final abort = Completer<void>();
    final request = http.AbortableRequest(
      method,
      baseUri.resolve(path),
      abortTrigger: cancelled == null
          ? abort.future
          : Future.any([abort.future, cancelled]),
    );
    if (body != null) {
      request.headers['content-type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    final response = await _http
        .send(request)
        .then(http.Response.fromStream)
        .timeout(
          timeout,
          onTimeout: () {
            abort.complete();
            throw TimeoutException('$method $path timed out.');
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
    return response.body.isEmpty ? null : jsonDecode(response.body);
  }

  /// Sends only the new user message; the server owns conversation history.
  Future<Map<String, dynamic>> readWorkspaceConversation(
    String workspaceId,
    String threadId,
  ) async => Map<String, dynamic>.from(
    await _tableRequest(
      'GET',
      '/workspaces/${Uri.encodeComponent(workspaceId)}/conversations/${Uri.encodeComponent(threadId)}',
    ) as Map,
  );

  /// Canceling the subscription aborts both pending HTTP and response streaming.
  Stream<AgentEvent> runAgent({
    required String workspaceId,
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
                  'workspaceId': workspaceId,
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

  Future<InboxSnapshot> readInbox(String workspaceId) async {
    final response = await _request(
      'GET',
      '/inbox/${Uri.encodeComponent(workspaceId)}',
      timeout: const Duration(seconds: 10),
    );
    return InboxSnapshot.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Future<void> resolveInboxItem(String workspaceId, String itemId) async {
    await _request(
      'POST',
      '/inbox/${Uri.encodeComponent(workspaceId)}/items/${Uri.encodeComponent(itemId)}/resolve',
      timeout: const Duration(seconds: 10),
    );
  }

  /// Emits the snapshot first, then a fresh snapshot after every pushed change.
  Stream<InboxSnapshot> watchInbox(String workspaceId) {
    final abort = Completer<void>();
    StreamSubscription<String>? incoming;
    late StreamController<InboxSnapshot> controller;
    controller = StreamController<InboxSnapshot>(
      onListen: () async {
        try {
          final request = http.AbortableRequest(
            'GET',
            baseUri.resolve(
              '/inbox/${Uri.encodeComponent(workspaceId)}/events',
            ),
            abortTrigger: abort.future,
          )..headers['accept'] = 'text/event-stream';
          final response = await _http.send(request);
          if (abort.isCompleted || response.statusCode != 200) {
            await response.stream.listen(null).cancel();
            if (!abort.isCompleted) {
              throw StateError(
                'Inbox events are unavailable (${response.statusCode}).',
              );
            }
            return;
          }
          String? event;
          final data = <String>[];
          incoming = response.stream
              .transform(utf8.decoder)
              .transform(const LineSplitter())
              .listen(
                (line) {
                  if (line.startsWith('event:')) {
                    event = line.substring(6).trim();
                  }
                  if (line.startsWith('data:')) {
                    data.add(line.substring(5).trimLeft());
                  }
                  if (line.isEmpty) {
                    final payload = data.join('\n');
                    final name = event;
                    event = null;
                    data.clear();
                    if (payload.isEmpty) return;
                    try {
                      if (name == 'snapshot') {
                        controller.add(
                          InboxSnapshot.fromJson(
                            jsonDecode(payload) as Map<String, dynamic>,
                          ),
                        );
                      } else {
                        unawaited(
                          readInbox(workspaceId)
                              .then(controller.add)
                              .catchError((Object error, StackTrace stack) {
                                controller.addError(error, stack);
                              }),
                        );
                      }
                    } catch (error, stack) {
                      controller.addError(error, stack);
                    }
                  }
                },
                onError: (Object error, StackTrace stack) {
                  if (!abort.isCompleted) controller.addError(error, stack);
                },
                onDone: controller.close,
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

  Future<Map<String, dynamic>> readUi(
    String workspace,
    String collection,
    String name,
  ) async {
    final response = await _request(
      'GET',
      '/workspaces/${Uri.encodeComponent(workspace)}/ui/$collection/${Uri.encodeComponent(name)}',
      timeout: const Duration(seconds: 10),
    );
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  Future<void> postUi(String path, {Map<String, Object?>? body}) async {
    await _request(
      'POST',
      path,
      body: body,
      timeout: const Duration(seconds: 10),
    );
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

  Future<Map<String, dynamic>> appRequest(
    String workspace,
    String path, {
    Map<String, Object?>? body,
  }) async {
    final response = await _request(
      body == null ? 'GET' : 'POST',
      '/workspaces/${Uri.encodeComponent(workspace)}/apps/$path',
      body: body,
      timeout: const Duration(seconds: 30),
    );
    return Map<String, dynamic>.from(jsonDecode(response.body) as Map);
  }

  Future<Map<String, dynamic>> behaviorRequest(
    String workspace,
    String path, {
    Map<String, Object?>? body,
  }) async {
    final response = await _request(
      body == null ? 'GET' : 'POST',
      '/workspaces/${Uri.encodeComponent(workspace)}/behaviors/$path',
      body: body,
      timeout: const Duration(seconds: 30),
    );
    return response.body.isEmpty
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(jsonDecode(response.body) as Map);
  }

  Future<Map<String, dynamic>> myDataRequest(
    String owner,
    String path, {
    Map<String, Object?>? body,
  }) async {
    final response = await _request(
      body == null ? 'GET' : 'POST',
      '/my-data/${Uri.encodeComponent(owner)}${path.isEmpty ? '' : '/$path'}',
      body: body,
      timeout: const Duration(seconds: 30),
    );
    return response.body.isEmpty
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(jsonDecode(response.body) as Map);
  }

  Future<Uint8List> appAsset(String workspace, String assetId) async {
    final response = await _request(
      'GET',
      '/workspaces/${Uri.encodeComponent(workspace)}/apps/assets/${Uri.encodeComponent(assetId)}',
      timeout: const Duration(seconds: 30),
    );
    return response.bodyBytes;
  }

  Future<Map<String, dynamic>> saveImageCopy(
    String workspace,
    String documentId,
    String operationId,
    Uint8List bytes,
  ) async {
    final request = http.Request(
      'POST',
      baseUri.resolve(
        '/workspaces/${Uri.encodeComponent(workspace)}/apps/images/$documentId/save/$operationId',
      ),
    );
    request.headers['content-type'] = 'image/png';
    request.bodyBytes = bytes;
    final response = await http.Response.fromStream(
      await _http.send(request).timeout(const Duration(seconds: 60)),
    ).timeout(const Duration(seconds: 60));
    if (response.statusCode != 200)
      throw StateError('Save failed: ${response.body}');
    return Map<String, dynamic>.from(jsonDecode(response.body) as Map);
  }

  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
