import 'dart:convert';

import 'package:http/http.dart' as http;

/// The containerized bundle carries no baked defines; the AppHost writes
/// config.json next to it with the kernel URL it allocated. An absent or
/// malformed file is a normal state (deployed bundles have none), never an error.
Future<Map<String, String>?> fetchServedConfig(http.Client? client) async {
  final effective = client ?? http.Client();
  try {
    final response = await effective.get(Uri.base.resolve('config.json'));
    if (response.statusCode != 200) {
      return null;
    }
    final decoded = jsonDecode(response.body);
    if (decoded is! Map) {
      return null;
    }
    return decoded.map((key, value) => MapEntry('$key', '$value'));
  } catch (_) {
    return null;
  } finally {
    if (client == null) {
      effective.close();
    }
  }
}
