import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_card.dart';
import 'package:ino_flutter/screens/shell/storyboard.dart';
import 'package:ino_flutter/screens/shell/storyboard_cards.dart';

void main() {
  group('Storyboard.parse', () {
    test('parses all four event kinds from tokyo-shaped JSON', () {
      const json = '''
      {
        "id": "tokyo",
        "label": "Test",
        "duration_s": 1.0,
        "events": [
          { "t": 0.0,  "kind": "orb",  "state": "listening" },
          { "t": 0.0,  "kind": "utter","text": "hello" },
          { "t": 0.5,  "kind": "syn",  "from": "A", "to": "B", "payload": {"k": "v"}, "gold": false },
          { "t": 0.8,  "kind": "card", "id": "flights", "stage": "enter", "from": "travel" }
        ]
      }
      ''';
      final sb = Storyboard.parse(json);

      expect(sb.id, 'tokyo');
      expect(sb.label, 'Test');
      expect(sb.durationSeconds, 1.0);
      expect(sb.events, hasLength(4));
      expect(sb.events[0], isA<OrbEvent>());
      expect(sb.events[1], isA<UtterEvent>());
      expect(sb.events[2], isA<SynapseEvent>());
      expect(sb.events[3], isA<CardEvent>());
    });

    test('OrbEvent carries state string', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 0, "kind": "orb", "state": "thinking" }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as OrbEvent;
      expect(ev.state, 'thinking');
      expect(ev.t, 0.0);
    });

    test('UtterEvent carries text', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 0.1, "kind": "utter", "text": "Plan a trip" }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as UtterEvent;
      expect(ev.text, 'Plan a trip');
      expect(ev.t, closeTo(0.1, 0.001));
    });

    test('SynapseEvent gold field defaults to false when absent', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 0, "kind": "syn", "from": "A", "to": "B", "payload": {} }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as SynapseEvent;
      expect(ev.gold, isFalse);
      expect(ev.from, 'A');
      expect(ev.to, 'B');
    });

    test('SynapseEvent gold:true is preserved', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 2.0, "kind": "syn", "from": "Preferences", "to": "PlanTrip",
                     "payload": {"ryokanBias": 0.62}, "gold": true }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as SynapseEvent;
      expect(ev.gold, isTrue);
      expect(ev.payload['ryokanBias'], 0.62);
    });

    test('CardEvent enter with from cluster', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 3.0, "kind": "card", "id": "flights", "stage": "enter", "from": "travel" }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as CardEvent;
      expect(ev.id, 'flights');
      expect(ev.stage, 'enter');
      expect(ev.fromCluster, 'travel');
    });

    test('CardEvent morph with null from', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 1.2, "kind": "card", "id": "hotels", "stage": "morph", "from": null }] }
      ''';
      final sb = Storyboard.parse(json);
      final ev = sb.events[0] as CardEvent;
      expect(ev.stage, 'morph');
      expect(ev.fromCluster, isNull);
    });

    test('throws FormatException on unknown event kind', () {
      const json = '''
      { "id": "x", "label": "", "duration_s": 0,
        "events": [{ "t": 0, "kind": "unknown_kind" }] }
      ''';
      expect(() => Storyboard.parse(json), throwsFormatException);
    });

    test('parses full tokyo event list — 16 events', () {
      const json = r'''
      {
        "id": "tokyo",
        "label": "Plan a 5-day Tokyo trip in late October",
        "duration_s": 6.6,
        "events": [
          { "t": 0.0,  "kind": "orb",  "state": "listening" },
          { "t": 0.0,  "kind": "utter","text": "Plan a 5-day Tokyo trip in late October, rain-friendly, mid-budget, leave from Kyiv." },
          { "t": 1.2,  "kind": "orb",  "state": "thinking" },
          { "t": 1.2,  "kind": "syn",  "from": "Cortex",      "to": "PlanTrip",    "payload": {"intent":"plan_trip","city":"Tokyo"}, "gold": false },
          { "t": 1.6,  "kind": "syn",  "from": "PlanTrip",    "to": "FindFlights", "payload": {"from":"KBP","to":"NRT"}, "gold": false },
          { "t": 1.62, "kind": "syn",  "from": "PlanTrip",    "to": "FindHotels",  "payload": {"city":"Tokyo"}, "gold": false },
          { "t": 1.64, "kind": "syn",  "from": "PlanTrip",    "to": "FindPlaces",  "payload": {"mood":"rain-friendly"}, "gold": false },
          { "t": 2.0,  "kind": "syn",  "from": "Preferences", "to": "PlanTrip",    "payload": {"ryokanBias":0.62}, "gold": true },
          { "t": 2.4,  "kind": "syn",  "from": "Forecast",    "to": "PlanTrip",    "payload": {}, "gold": false },
          { "t": 3.0,  "kind": "card", "id": "flights",   "stage": "enter", "from": "travel" },
          { "t": 3.8,  "kind": "card", "id": "hotels",    "stage": "enter", "from": "travel" },
          { "t": 4.6,  "kind": "card", "id": "itinerary", "stage": "enter", "from": "travel" },
          { "t": 5.4,  "kind": "syn",  "from": "PlanTrip", "to": "VisaReminder", "payload": {}, "gold": false },
          { "t": 5.5,  "kind": "card", "id": "reminder",  "stage": "enter", "from": "reminders" },
          { "t": 6.0,  "kind": "orb",  "state": "celebrating" },
          { "t": 6.2,  "kind": "orb",  "state": "idle" }
        ]
      }
      ''';
      final sb = Storyboard.parse(json);
      expect(sb.events, hasLength(16));
      expect(sb.durationSeconds, closeTo(6.6, 0.001));
    });
  });

  group('StoryboardCards', () {
    test('resolve returns correct model for each known id', () {
      expect(StoryboardCards.resolve('flights'), isNotNull);
      expect(StoryboardCards.resolve('hotels'), isNotNull);
      expect(StoryboardCards.resolve('itinerary'), isNotNull);
      expect(StoryboardCards.resolve('reminder'), isNotNull);
    });

    test('resolve returns null for unknown id', () {
      expect(StoryboardCards.resolve('unknown'), isNull);
    });

    test('flights has 3 rows, all FlightRow', () {
      final rows = StoryboardCards.flights.rows;
      expect(rows, hasLength(3));
      expect(rows.every((r) => r is FlightRow), isTrue);
    });

    test('hotels has 4 rows with one dimmed', () {
      final rows = StoryboardCards.hotels.rows;
      expect(rows, hasLength(4));
      final dimmed = rows.where((r) => r.dim).toList();
      expect(dimmed, hasLength(1));
      expect((dimmed.first as HotelRow).name, 'Mimaru Akasaka');
    });

    test('itinerary day 3 is highlighted', () {
      final rows = StoryboardCards.itinerary.rows;
      expect(rows, hasLength(5));
      final day3 = rows[2] as DayRow;
      expect(day3.highlight, isTrue);
      expect(day3.day, 'Day 3');
    });

    test('reminder has 1 ReminderRow', () {
      final rows = StoryboardCards.reminder.rows;
      expect(rows, hasLength(1));
      expect(rows.first, isA<ReminderRow>());
    });

    test('hotelsReplan has Sakura Ryokan highlighted', () {
      final rows = StoryboardCards.hotelsReplan.rows;
      expect(rows, hasLength(4));
      final sakura = rows[1] as HotelRow;
      expect(sakura.name, 'Sakura Ryokan');
      expect(sakura.highlight, isTrue);
    });

    test('hotelsReplan last row is dimmed', () {
      final rows = StoryboardCards.hotelsReplan.rows;
      expect(rows.last.dim, isTrue);
    });
  });

  // Timer scheduling — exercises Storyboard.parse + Timer ordering via FakeAsync.
  // Avoids constructing a DemoRunner to sidestep BLoC / canvas mocks; BLoC
  // integration is covered by T11.1's smoke.
  group('Storyboard timer scheduling', () {
    test('orb events fire in order at correct offsets', () {
      fakeAsync((async) {
        const json = '''
        { "id": "x", "label": "", "duration_s": 1.0, "events": [
          { "t": 0.1, "kind": "orb", "state": "listening" },
          { "t": 0.4, "kind": "orb", "state": "thinking" },
          { "t": 0.9, "kind": "orb", "state": "celebrating" }
        ]}
        ''';
        final sb = Storyboard.parse(json);
        final fired = <String>[];

        final timers = <Timer>[];
        for (final ev in sb.events) {
          final ms = (ev.t * 1000).round();
          timers.add(Timer(Duration(milliseconds: ms), () {
            fired.add((ev as OrbEvent).state);
          }));
        }

        async.elapse(const Duration(milliseconds: 50));
        expect(fired, isEmpty);

        async.elapse(const Duration(milliseconds: 60));
        expect(fired, ['listening']);

        async.elapse(const Duration(milliseconds: 300));
        expect(fired, ['listening', 'thinking']);

        async.elapse(const Duration(milliseconds: 600));
        expect(fired, ['listening', 'thinking', 'celebrating']);

        for (final t in timers) { t.cancel(); }
      });
    });

    test('cancel before elapse suppresses dispatch', () {
      fakeAsync((async) {
        const json = '''
        { "id": "x", "label": "", "duration_s": 0.5, "events": [
          { "t": 0.3, "kind": "orb", "state": "thinking" }
        ]}
        ''';
        final sb = Storyboard.parse(json);
        final fired = <String>[];

        final timers = <Timer>[];
        for (final ev in sb.events) {
          final ms = (ev.t * 1000).round();
          timers.add(Timer(Duration(milliseconds: ms), () {
            fired.add('fired');
          }));
        }

        for (final t in timers) { t.cancel(); }
        async.elapse(const Duration(milliseconds: 500));
        expect(fired, isEmpty);
      });
    });

    test('mixed event kinds schedule at correct offsets', () {
      fakeAsync((async) {
        const json = '''
        { "id": "mix", "label": "", "duration_s": 2.0, "events": [
          { "t": 0.0,  "kind": "orb",   "state": "listening" },
          { "t": 0.0,  "kind": "utter", "text": "hello" },
          { "t": 1.0,  "kind": "syn",   "from": "A", "to": "B", "payload": {}, "gold": false },
          { "t": 2.0,  "kind": "card",  "id": "flights", "stage": "enter", "from": "travel" }
        ]}
        ''';
        final sb = Storyboard.parse(json);
        final order = <String>[];

        final timers = <Timer>[];
        for (final ev in sb.events) {
          final ms = (ev.t * 1000).round();
          timers.add(Timer(Duration(milliseconds: ms), () {
            order.add(ev.runtimeType.toString());
          }));
        }

        async.elapse(const Duration(milliseconds: 0));
        // t=0 timers fire on elapse(0) after microtask flush
        async.flushMicrotasks();
        async.elapse(const Duration(milliseconds: 1));
        expect(order.where((s) => s == 'OrbEvent').length, 1);
        expect(order.where((s) => s == 'UtterEvent').length, 1);

        async.elapse(const Duration(milliseconds: 999));
        expect(order.where((s) => s == 'SynapseEvent').length, 1);

        async.elapse(const Duration(milliseconds: 1000));
        expect(order.where((s) => s == 'CardEvent').length, 1);

        for (final t in timers) { t.cancel(); }
      });
    });
  });
}
