import 'package:digitalbrain_flutter_shell/integrations/connect_window.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('masks the pasted value and never echoes it', (tester) async {
    final harness = _Harness();
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: ConnectWindow(request: harness.request))),
    );
    await tester.pumpAndSettle();

    final valueField = tester.widget<TextField>(
      find.byKey(const Key('connect_value')),
    );
    expect(valueField.obscureText, isTrue);

    await tester.enterText(find.byKey(const Key('connect_name')), 'mail');
    await tester.enterText(
      find.byKey(const Key('connect_value')),
      'canary-token-9a1f',
    );
    await tester.tap(find.byKey(const Key('connect_submit')));
    await tester.pumpAndSettle();

    expect(harness.calls.any((call) => call.$1 == 'connect'), isTrue);
    final connect = harness.calls.firstWhere((call) => call.$1 == 'connect');
    expect(connect.$2!['value'], 'canary-token-9a1f');
    expect(find.textContaining('canary-token-9a1f'), findsNothing);
    expect(
      tester
          .widget<TextField>(find.byKey(const Key('connect_value')))
          .controller!
          .text,
      isEmpty,
    );
    expect(find.textContaining('Connected'), findsWidgets);
  });

  testWidgets('lists status and probes and disconnects', (tester) async {
    final harness = _Harness()
      ..records = [
        {'id': 'db', 'source': 'supabase', 'status': 'Expired'},
      ];
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: ConnectWindow(request: harness.request))),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('supabase · db'), findsOneWidget);
    expect(find.textContaining('Expired'), findsOneWidget);

    await tester.tap(find.byTooltip('Probe connection'));
    await tester.pumpAndSettle();
    expect(harness.calls.any((call) => call.$1 == 'probe'), isTrue);

    await tester.tap(find.byTooltip('Disconnect'));
    await tester.pumpAndSettle();
    expect(harness.calls.any((call) => call.$1 == 'disconnect'), isTrue);
    expect(harness.records, isEmpty);
  });
}

final class _Harness {
  List<Map<String, dynamic>> records = [];
  final List<(String, Map<String, Object?>?)> calls = [];

  Future<Object?> request(String path, {Map<String, Object?>? body}) async {
    calls.add((path, body));
    switch (path) {
      case '':
        return records;
      case 'connect':
        records = [
          {'id': body!['connectionId'], 'source': body['source'], 'status': 'Connected'},
        ];
        return records.single;
      case 'disconnect':
        records = [];
        return null;
      default:
        return records.single;
    }
  }
}
