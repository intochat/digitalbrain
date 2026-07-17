import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/shell/app_shell.dart';
import 'package:workspace/theme/brain_theme.dart';

BrainGateway _emptyFeedGateway() {
  final client = MockClient((request) async {
    return http.Response(
      jsonEncode({'revision': 0, 'stateJson': '{"records":[]}'}),
      200,
    );
  });
  return BrainGateway(
    httpBase: 'http://gateway.test',
    wsBase: 'ws://gateway.test',
    client: client,
  );
}

void main() {
  testWidgets('renders all five destination labels', (tester) async {
    await tester.pumpWidget(
      MaterialApp(theme: BrainTheme.dark, home: AppShell(_emptyFeedGateway())),
    );
    await tester.pump();

    for (final label in [
      'Today',
      'Chat',
      'Abilities',
      'Connections',
      'Activity',
    ]) {
      expect(find.text(label), findsWidgets);
    }
  });

  testWidgets("Today's empty state shows Nothing needs you", (tester) async {
    await tester.pumpWidget(
      MaterialApp(theme: BrainTheme.dark, home: AppShell(_emptyFeedGateway())),
    );
    await tester.pumpAndSettle();

    expect(find.text('Nothing needs you.'), findsOneWidget);
  });

  testWidgets('theme smoke: app builds with BrainTheme.dark without error', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(theme: BrainTheme.dark, home: AppShell(_emptyFeedGateway())),
    );
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
  });
}
