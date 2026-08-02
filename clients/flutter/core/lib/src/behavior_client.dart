import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'behavior_models.dart';
import 'sse_behavior_frames.dart';

final class BehaviorClient {
  BehaviorClient({required this.baseUri, http.Client? httpClient})
    : _http = httpClient ?? http.Client(),
      _ownsClient = httpClient == null;

  final Uri baseUri;
  final http.Client _http;
  final bool _ownsClient;
  final _aborts = <Completer<void>>[];

  Future<BehaviorLibraryDocument> listBehaviors() async {
    final response = await _getJson('/behaviors');
    return BehaviorLibraryDocument.fromJson(response);
  }

  Future<BehaviorDocument> readBehavior(String behaviorId) async {
    final response = await _getJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}',
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> stop(String behaviorId) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/stop',
      body: null,
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> start(String behaviorId) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/start',
      body: null,
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorRunOnceResult> runOnce({
    required String behaviorId,
    required String triggerTypeName,
    required String triggerJson,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/run-once',
      body: {
        'triggerTypeName': triggerTypeName,
        'triggerJson': triggerJson,
      },
    );
    return BehaviorRunOnceResult.fromJson(response);
  }

  Future<BehaviorDocument> propose({
    required String behaviorId,
    required String programSource,
    required String featureText,
    String featureName = 'install',
    String displayName = '',
    String description = '',
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/propose',
      body: {
        'programSource': programSource,
        'featureText': featureText,
        'featureName': featureName,
        'displayName': displayName,
        'description': description,
      },
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> runTests({
    required String behaviorId,
    required String artifactHash,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/tests',
      body: {'artifactHash': artifactHash},
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> approve({
    required String behaviorId,
    required String artifactHash,
    required String approvalId,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/approve',
      body: {
        'artifactHash': artifactHash,
        'approvalId': approvalId,
      },
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> activate({
    required String behaviorId,
    required String artifactHash,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/activate',
      body: {'artifactHash': artifactHash},
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> rollback(String behaviorId) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/rollback',
      body: null,
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorDocument> setBindingEnabled({
    required String behaviorId,
    required String bindingId,
    required bool enabled,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/bindings/${Uri.encodeComponent(bindingId)}',
      body: {'enabled': enabled},
    );
    return BehaviorDocument.fromJson(response);
  }

  Future<BehaviorChangeProposal> proposeChange({
    required String behaviorId,
    required String requestText,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/change/propose',
      body: {'requestText': requestText},
    );
    return BehaviorChangeProposal.fromJson(response);
  }

  Future<Object> approveScenarioChange({
    required String behaviorId,
    required String proposalId,
    required bool approved,
    String? featureText,
    String? featureName,
  }) async {
    final response = await _postJson(
      '/behaviors/${Uri.encodeComponent(behaviorId)}/change/approve',
      body: {
        'proposalId': proposalId,
        'approved': approved,
        if (featureText != null) 'featureText': featureText,
        if (featureName != null) 'featureName': featureName,
      },
    );
    if (response['proposalId'] is String) {
      return BehaviorChangeProposal.fromJson(response);
    }
    return BehaviorDocument.fromJson(response);
  }

  Stream<BehaviorEvent> watchEvents({
    required String behaviorId,
    int afterSequence = 0,
  }) async* {
    final uri = baseUri.replace(
      path: '/behaviors/${Uri.encodeComponent(behaviorId)}/events',
      queryParameters: {'afterSequence': '$afterSequence'},
    );
    final abort = Completer<void>();
    _aborts.add(abort);
    final request = http.AbortableRequest(
      'GET',
      uri,
      abortTrigger: abort.future,
    );
    try {
      final response = await _http.send(request);
      if (response.statusCode != 200) {
        throw StateError('behavior events failed: ${response.statusCode}');
      }

      final lines = response.stream
          .transform(utf8.decoder)
          .transform(const LineSplitter());

      final parser = SseBehaviorParser();
      await for (final line in lines) {
        for (final event in parser.addLine(line)) {
          yield event;
        }
      }
      for (final event in parser.flush()) {
        yield event;
      }
    } finally {
      _aborts.remove(abort);
    }
  }

  Future<void> cancelInflight() async {
    for (final abort in List<Completer<void>>.of(_aborts)) {
      if (!abort.isCompleted) {
        abort.complete();
      }
    }
    _aborts.clear();
  }

  void close() {
    unawaited(cancelInflight());
    if (_ownsClient) {
      _http.close();
    }
  }

  Future<Map<String, Object?>> _getJson(String path) async {
    final uri = baseUri.replace(path: path);
    final abort = Completer<void>();
    _aborts.add(abort);
    final request = http.AbortableRequest(
      'GET',
      uri,
      abortTrigger: abort.future,
    );
    try {
      final streamed = await _http.send(request);
      final response = await http.Response.fromStream(streamed);
      if (response.statusCode != 200) {
        throw StateError(
          'GET $path failed: ${response.statusCode} ${response.body}',
        );
      }
      return _decodeObject(response.body);
    } finally {
      _aborts.remove(abort);
    }
  }

  Future<Map<String, Object?>> _postJson(
    String path, {
    required Map<String, Object?>? body,
  }) async {
    final uri = baseUri.replace(path: path);
    final abort = Completer<void>();
    _aborts.add(abort);
    final request = http.AbortableRequest(
      'POST',
      uri,
      abortTrigger: abort.future,
    )..headers['content-type'] = 'application/json';
    if (body != null) {
      request.body = jsonEncode(body);
    }
    try {
      final streamed = await _http.send(request);
      final response = await http.Response.fromStream(streamed);
      if (response.statusCode != 200) {
        throw StateError(
          'POST $path failed: ${response.statusCode} ${response.body}',
        );
      }
      if (response.body.isEmpty) {
        return const {};
      }
      return _decodeObject(response.body);
    } finally {
      _aborts.remove(abort);
    }
  }

  static Map<String, Object?> _decodeObject(String body) {
    final decoded = jsonDecode(body);
    if (decoded is! Map) {
      throw const FormatException('expected JSON object');
    }
    return Map<String, Object?>.from(decoded);
  }
}
