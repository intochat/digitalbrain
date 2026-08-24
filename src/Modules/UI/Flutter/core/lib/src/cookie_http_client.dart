import 'package:http/http.dart' as http;

import 'basic_credentials.dart';

/// Captures Set-Cookie from the kernel and re-sends Cookie on later calls.
/// Loopback dev auth does not need cookies once a bootstrap owner exists, but
/// login/bootstrap cookies keep Flutter working when loopback cannot apply.
final class CookieHttpClient extends http.BaseClient {
  CookieHttpClient(this._inner, {this.credentials});

  final http.Client _inner;
  final Map<String, String> _cookies = {};

  /// Attached to every outgoing request. The single choke point every call
  /// funnels through, so SSE streams and multipart voice uploads are covered
  /// without touching their own send sites.
  final BasicCredentials? credentials;

  Map<String, String> get cookies => Map.unmodifiable(_cookies);

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    final credential = credentials;
    if (credential != null && !request.headers.containsKey('authorization')) {
      request.headers['authorization'] = credential.authorizationHeader;
    }

    if (_cookies.isNotEmpty && !request.headers.containsKey('cookie')) {
      request.headers['cookie'] = _cookies.entries
          .map((entry) => '${entry.key}=${entry.value}')
          .join('; ');
    }

    final response = await _inner.send(request);
    _captureCookies(response.headers);
    return response;
  }

  void _captureCookies(Map<String, String> headers) {
    // package:http lower-cases header names; multiple Set-Cookie may be joined.
    final raw = headers['set-cookie'];
    if (raw == null || raw.isEmpty) {
      return;
    }

    for (final part in _splitSetCookie(raw)) {
      final pair = part.split(';').first.trim();
      final eq = pair.indexOf('=');
      if (eq <= 0) {
        continue;
      }
      final name = pair.substring(0, eq).trim();
      final value = pair.substring(eq + 1).trim();
      if (name.isEmpty) {
        continue;
      }
      _cookies[name] = value;
    }
  }

  /// Best-effort split when multiple Set-Cookie headers were folded into one.
  static Iterable<String> _splitSetCookie(String raw) {
    // Commas separate cookies only when not inside Expires=... date values.
    final result = <String>[];
    var start = 0;
    var i = 0;
    while (i < raw.length) {
      if (raw[i] == ',') {
        final slice = raw.substring(start, i);
        // Expires=Wed, 26 Aug … — keep reading if this looks like a date comma.
        if (RegExp(
          r'expires\s*=\s*\w+$',
          caseSensitive: false,
        ).hasMatch(slice.trimRight())) {
          i++;
          continue;
        }
        result.add(slice.trim());
        start = i + 1;
      }
      i++;
    }
    final tail = raw.substring(start).trim();
    if (tail.isNotEmpty) {
      result.add(tail);
    }
    return result;
  }

  @override
  void close() => _inner.close();
}
