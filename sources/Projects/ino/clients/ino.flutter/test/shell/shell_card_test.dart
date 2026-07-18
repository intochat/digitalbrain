import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_card.dart';

ShellCardModel cardModel(List<ShellCardRow> rows) => ShellCardModel(
      id: 'flights',
      title: 'Flights · Kyiv → Tokyo',
      subtitle: 'mid-budget · 3 candidates',
      cluster: 'travel',
      rows: rows,
    );

void main() {
  testWidgets('header renders cluster, title, subtitle', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(body: ShellCard(model: cardModel(const []))),
    ));
    expect(find.text('TRAVEL'), findsOneWidget);
    expect(find.text('Flights · Kyiv → Tokyo'), findsOneWidget);
    expect(find.text('mid-budget · 3 candidates'), findsOneWidget);
  });

  testWidgets('flight row shows code, route, duration, price, tag', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ShellCard(
          model: cardModel(const [
            FlightRow(
              code: 'TK 762',
              route: 'KBP → IST → NRT',
              duration: '15h 25m',
              price: '\$612',
              tag: 'best value',
            ),
          ]),
        ),
      ),
    ));
    expect(find.text('TK 762'), findsOneWidget);
    expect(find.text('KBP → IST → NRT'), findsOneWidget);
    expect(find.text('15h 25m'), findsOneWidget);
    expect(find.text('\$612'), findsOneWidget);
    expect(find.text('best value'), findsOneWidget);
  });

  testWidgets('hotel row honors dim with reduced opacity', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ShellCard(
          model: cardModel(const [
            HotelRow(
              name: 'Mimaru Akasaka',
              area: 'Akasaka',
              note: 'chain · skipped',
              price: '—',
              tag: 'dimmed',
              dim: true,
            ),
          ]),
        ),
      ),
    ));
    final opacity = tester.widget<Opacity>(find.ancestor(
      of: find.text('Mimaru Akasaka'),
      matching: find.byType(Opacity),
    ).first);
    expect(opacity.opacity, 0.45);
  });

  testWidgets('day row applies highlight tone on rainy anchor', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ShellCard(
          model: cardModel(const [
            DayRow(
              day: 'Day 3',
              weather: '78%',
              plan: 'TeamLab Planets · onsen evening',
              highlight: true,
            ),
          ]),
        ),
      ),
    ));
    expect(find.text('Day 3'), findsOneWidget);
    expect(find.text('78%'), findsOneWidget);
    expect(find.textContaining('TeamLab'), findsOneWidget);
  });

  testWidgets('reminder row renders compactly', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ShellCard(
          model: cardModel(const [
            ReminderRow(
              name: 'Check visa requirements',
              when: 'in 3 days',
              tag: 'auto · accept?',
            ),
          ]),
        ),
      ),
    ));
    expect(find.text('Check visa requirements'), findsOneWidget);
    expect(find.text('in 3 days'), findsOneWidget);
    expect(find.text('auto · accept?'), findsOneWidget);
  });

  testWidgets('chevron taps invoke onReplayTrace', (tester) async {
    var taps = 0;
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ShellCard(
          model: cardModel(const []),
          onReplayTrace: () => taps++,
        ),
      ),
    ));
    await tester.tap(find.byIcon(Icons.chevron_right));
    expect(taps, 1);
  });
}
