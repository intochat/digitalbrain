import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'product_models.dart';

abstract interface class DigitalBrainProductApi {
  Future<List<ProductModule>> getModules();
  Future<List<ProductOperation>> getOperations();
  Future<ProductActivityReceipt> invoke(
    String operationId,
    Map<String, Object?> input, {
    required String idempotencyKey,
  });
  Future<ProductActivity> getActivity(String activityId);
  Stream<ProductActivity> watchActivity(String activityId, {int afterSequence});
  Future<ChatTurnEnvelope> sendChat(
    String message, {
    required String idempotencyKey,
  });
  Future<BrainJournalPage> getJournal(String activityId, {int afterSequence});
  Stream<BrainJournalRecord> watchJournal(
    String activityId, {
    int afterSequence,
  });
  Future<BrainSnapshot> getBrain();
  Stream<BrainSnapshot> watchBrain({int afterSequence});
  void close();
}

final class DigitalBrainProductClient implements DigitalBrainProductApi {
  DigitalBrainProductClient({required this.baseUri, http.Client? httpClient})
    : _http = httpClient ?? http.Client(),
      _ownsClient = httpClient == null;
  final Uri baseUri;
  final http.Client _http;
  final bool _ownsClient;

  @override
  Future<List<ProductModule>> getModules() =>
      _list('/v2/modules', 'module discovery', ProductModule.fromJson);
  @override
  Future<List<ProductOperation>> getOperations() =>
      _list('/v2/operations', 'operation discovery', ProductOperation.fromJson);

  @override
  Future<ProductActivityReceipt> invoke(
    String operationId,
    Map<String, Object?> input, {
    required String idempotencyKey,
  }) async {
    final response = await _http.post(
      _uri('/v2/operations/${Uri.encodeComponent(operationId)}:invoke'),
      headers: {
        'content-type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: jsonEncode({'input': input}),
    );
    return ProductActivityReceipt.fromJson(
      _requireObject(response, 'operation invocation', expected: 202),
    );
  }

  @override
  Future<ProductActivity> getActivity(String activityId) async {
    final response = await _http.get(_uri('/v2/activities/$activityId'));
    return ProductActivity.fromJson(_requireObject(response, 'activity read'));
  }

  @override
  Stream<ProductActivity> watchActivity(
    String activityId, {
    int afterSequence = 0,
  }) => _events(
    '/v2/activities/$activityId/events',
    afterSequence,
    ProductActivity.fromJson,
  );

  @override
  Future<ChatTurnEnvelope> sendChat(
    String message, {
    required String idempotencyKey,
  }) async {
    final response = await _http.post(
      _uri('/v2/chat'),
      headers: {
        'content-type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: jsonEncode({'message': message}),
    );
    return ChatTurnEnvelope.fromJson(_requireObject(response, 'chat'));
  }

  @override
  Future<BrainJournalPage> getJournal(
    String activityId, {
    int afterSequence = 0,
  }) async {
    final response = await _http.get(
      _uri(
        '/v2/activities/$activityId/journal?afterSequence=$afterSequence&take=500',
      ),
    );
    return BrainJournalPage.fromJson(_requireObject(response, 'journal read'));
  }

  @override
  Stream<BrainJournalRecord> watchJournal(
    String activityId, {
    int afterSequence = 0,
  }) => _events(
    '/v2/activities/$activityId/journal/events',
    afterSequence,
    BrainJournalRecord.fromJson,
  );

  @override
  Future<BrainSnapshot> getBrain() async {
    final response = await _http.get(_uri('/v2/brain'));
    return BrainSnapshot.fromJson(_requireObject(response, 'brain read'));
  }

  @override
  Stream<BrainSnapshot> watchBrain({int afterSequence = 0}) =>
      _events('/v2/brain/events', afterSequence, BrainSnapshot.fromJson);

  Future<List<T>> _list<T>(
    String path,
    String operation,
    T Function(Map<String, Object?>) parse,
  ) async {
    final response = await _http.get(_uri(path));
    return _requireList(response, operation)
        .map((value) => parse(Map<String, Object?>.from(value as Map)))
        .toList(growable: false);
  }

  Stream<T> _events<T>(
    String path,
    int afterSequence,
    T Function(Map<String, Object?>) parse,
  ) async* {
    final request = http.Request('GET', _uri(path))
      ..headers['accept'] = 'text/event-stream';
    if (afterSequence > 0) {
      request.headers['Last-Event-ID'] = '$afterSequence';
    }
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      throw StateError('$path events failed: ${response.statusCode}');
    }
    await for (final line
        in response.stream
            .transform(utf8.decoder)
            .transform(const LineSplitter())) {
      if (!line.startsWith('data:')) {
        continue;
      }
      final decoded = jsonDecode(line.substring('data:'.length).trim());
      if (decoded is Map) {
        yield parse(Map<String, Object?>.from(decoded));
      }
    }
  }

  Uri _uri(String path) => Uri.parse('${baseUri.origin}$path');
  static List<Object?> _requireList(http.Response response, String operation) {
    if (response.statusCode != 200) {
      throw StateError(
        '$operation failed: ${response.statusCode} ${response.body}',
      );
    }
    final value = jsonDecode(response.body);
    if (value is! List) {
      throw FormatException('$operation is not a list');
    }
    return value;
  }

  static Map<String, Object?> _requireObject(
    http.Response response,
    String operation, {
    int expected = 200,
  }) {
    if (response.statusCode != expected) {
      throw StateError(
        '$operation failed: ${response.statusCode} ${response.body}',
      );
    }
    final value = jsonDecode(response.body);
    if (value is! Map) {
      throw FormatException('$operation is not an object');
    }
    return Map<String, Object?>.from(value);
  }

  @override
  void close() {
    if (_ownsClient) _http.close();
  }
}
