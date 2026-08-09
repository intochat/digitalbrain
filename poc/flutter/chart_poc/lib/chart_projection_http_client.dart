import 'dart:convert';
import 'dart:io';

import 'chart_projection.dart';

final class ChartProjectionHttpClient implements ChartProjection {
  ChartProjectionHttpClient({
    required Uri baseUri,
    required String chartId,
    required String opaqueSessionToken,
  }) : _uri = baseUri.replace(
         pathSegments: [
           ...baseUri.pathSegments.where((segment) => segment.isNotEmpty),
           'poc',
           'charts',
           chartId,
         ],
       ),
       _opaqueSessionToken = opaqueSessionToken {
    if (chartId.trim().isEmpty) {
      throw ArgumentError.value(
        chartId,
        'chartId',
        'Chart ID cannot be empty.',
      );
    }
    if (opaqueSessionToken.trim().isEmpty) {
      throw ArgumentError.value(
        opaqueSessionToken,
        'opaqueSessionToken',
        'Opaque session token cannot be empty.',
      );
    }
  }

  final Uri _uri;
  final String _opaqueSessionToken;

  @override
  Future<List<ChartPointView>> loadPoints() async {
    final client = HttpClient();
    try {
      final request = await client.getUrl(_uri);
      request.headers.set(
        HttpHeaders.authorizationHeader,
        'Bearer $_opaqueSessionToken',
      );
      final response = await request.close();
      final body = await utf8.decoder.bind(response).join();
      if (response.statusCode != HttpStatus.ok) {
        throw HttpException(
          'Chart projection returned HTTP ${response.statusCode}.',
          uri: _uri,
        );
      }

      final payload = jsonDecode(body);
      if (payload is! Map<String, Object?> || payload['points'] is! List) {
        throw const FormatException('Chart projection response is malformed.');
      }

      return (payload['points']! as List<Object?>)
          .map((point) {
            if (point is! Map<String, Object?>) {
              throw const FormatException(
                'Chart projection point is malformed.',
              );
            }
            return ChartPointView.fromJson(point);
          })
          .toList(growable: false);
    } finally {
      client.close(force: true);
    }
  }
}
