import 'dart:convert';

import 'package:http/http.dart' as http;

class TelegramApi {
  TelegramApi(this.initData, {http.Client? client, Uri? origin})
    : _client = client ?? http.Client(),
      _origin = origin ?? Uri.base;

  final String initData;
  final http.Client _client;
  final Uri _origin;
  Map<String, String> get _headers => {
    'Authorization': 'tma $initData',
    'Content-Type': 'application/json',
  };

  Future<Map<String, dynamic>> state() async {
    if (initData.isEmpty) throw StateError('Open this app from Telegram.');
    final response = await _client.get(
      _origin.resolve('/telegram/miniapp/state'),
      headers: _headers,
    );
    _check(response);
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  Future<void> dismiss(String eventId) =>
      _post('notifications/dismiss', {'eventId': eventId});
  Future<void> cancel(String reminderId) =>
      _post('reminders/cancel', {'reminderId': reminderId});

  Future<void> _post(String action, Map<String, String> body) async {
    if (initData.isEmpty) throw StateError('Open this app from Telegram.');
    _check(
      await _client.post(
        _origin.resolve('/telegram/miniapp/$action'),
        headers: _headers,
        body: jsonEncode(body),
      ),
    );
  }

  void _check(http.Response response) {
    if (response.statusCode == 401) {
      throw StateError(
        'Your Telegram session expired. Close and reopen this app.',
      );
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw StateError('Unable to update your inbox. Please retry.');
    }
  }

  void close() => _client.close();
}
