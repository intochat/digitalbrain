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
  Future<List<ProductModule>> getModules() async {
    final response = await _http.get(_uri('/v2/modules'));
    final values = _requireList(response, 'module discovery');
    return values
        .map(
          (value) =>
              ProductModule.fromJson(Map<String, Object?>.from(value as Map)),
        )
        .toList(growable: false);
  }

  @override
  Future<List<ProductOperation>> getOperations() async {
    final response = await _http.get(_uri('/v2/operations'));
    final values = _requireList(response, 'operation discovery');
    return values
        .map(
          (value) => ProductOperation.fromJson(
            Map<String, Object?>.from(value as Map),
          ),
        )
        .toList(growable: false);
  }

  @override
  Future<ProductActivityReceipt> invoke(
    String operationId,
    Map<String, Object?> input, {
    required String idempotencyKey,
  }) async {
    final encoded = Uri.encodeComponent(operationId);
    final response = await _http.post(
      _uri('/v2/operations/$encoded:invoke'),
      headers: {
        'content-type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: jsonEncode({'input': input}),
    );
    final value = _requireObject(
      response,
      'operation invocation',
      expected: 202,
    );
    return ProductActivityReceipt.fromJson(value);
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
  }) async* {
    final request = http.Request(
      'GET',
      _uri('/v2/activities/$activityId/events'),
    )..headers['accept'] = 'text/event-stream';
    if (afterSequence > 0) {
      request.headers['Last-Event-ID'] = '$afterSequence';
    }

    final response = await _http.send(request);
    if (response.statusCode != 200) {
      throw StateError('activity events failed: ${response.statusCode}');
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
        yield ProductActivity.fromJson(Map<String, Object?>.from(decoded));
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
      throw FormatException('$operation response is not a list');
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
      throw FormatException('$operation response is not an object');
    }
    return Map<String, Object?>.from(value);
  }

  @override
  void close() {
    if (_ownsClient) {
      _http.close();
    }
  }
}
