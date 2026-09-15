import 'dart:convert';

import 'package:digitalbrain_telegram/api.dart';
import 'package:digitalbrain_telegram/app.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets('shows stopped automation and processing errors', (tester) async {
    final api = TelegramApi(
      'signed',
      client: MockClient(
        (_) async => http.Response(
          '{"notifications":[],"reminders":[],"automation":{"status":"Stopped","hasErrors":true}}',
          200,
        ),
      ),
    );
    await tester.pumpWidget(TelegramApp(initData: 'signed', api: api));
    await tester.pumpAndSettle();
    expect(
      find.textContaining('Reminder processing needs attention'),
      findsOneWidget,
    );
    expect(find.textContaining('Automation stopped'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });
  testWidgets('requires Telegram launch data before sending requests', (
    tester,
  ) async {
    await tester.pumpWidget(const TelegramApp(initData: ''));
    expect(find.text('Open this app from your Telegram bot.'), findsOneWidget);
  });

  test(
    'same origin API sends only signed authorization and typed action identity',
    () async {
      final requests = <http.Request>[];
      final api = TelegramApi(
        'signed-data',
        origin: Uri.parse('https://brain.test/telegram/app/'),
        client: MockClient((request) async {
          requests.add(request);
          return http.Response(
            request.method == 'GET'
                ? '{"notifications":[],"reminders":[]}'
                : '',
            200,
          );
        }),
      );
      await api.state();
      await api.dismiss('event-1');
      await api.cancel('reminder-2');
      expect(
        requests.every(
          (r) =>
              r.url.origin == 'https://brain.test' &&
              r.headers['Authorization'] == 'tma signed-data',
        ),
        isTrue,
      );
      expect(jsonDecode(requests[1].body), {'eventId': 'event-1'});
      expect(jsonDecode(requests[2].body), {'reminderId': 'reminder-2'});
      api.close();
    },
  );

  testWidgets('renders inbox and scheduled reminders, then sends cancel', (
    tester,
  ) async {
    final requests = <http.Request>[];
    final api = TelegramApi(
      'signed',
      client: MockClient((request) async {
        requests.add(request);
        return http.Response(
          jsonEncode({
            'notifications': [
              {
                'eventId': 'e',
                'title': 'Incoming message',
                'message': '<b>Call Alice</b>',
                'kind': 'incoming',
                'createdUnixSeconds': 1800000000,
                'dismissed': false,
              },
            ],
            'reminders': [
              {
                'reminderId': 'r',
                'text': 'Call Alice',
                'dueUnixSeconds': 1800000600,
                'status': 'Scheduled',
              },
            ],
          }),
          200,
        );
      }),
    );
    await tester.pumpWidget(TelegramApp(initData: 'signed', api: api));
    await tester.pumpAndSettle();
    expect(find.text('<b>Call Alice</b>'), findsOneWidget);
    expect(find.text('Call Alice'), findsOneWidget);
    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();
    expect(
      requests.where((r) => r.method == 'POST').single.body,
      '{"reminderId":"r"}',
    );
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('expired identity shows reopen instruction', (tester) async {
    final api = TelegramApi(
      'expired',
      client: MockClient((_) async => http.Response('', 401)),
    );
    await tester.pumpWidget(TelegramApp(initData: 'expired', api: api));
    await tester.pumpAndSettle();
    expect(find.textContaining('Close and reopen'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });
}
