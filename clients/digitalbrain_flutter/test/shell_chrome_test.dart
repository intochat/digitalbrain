import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter/src/shell_chrome.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'ShellSurfaceApp lists projected scenes by sceneKey and title',
    (tester) async {
      final surface = ShellSurfaceController();
      surface.apply(
        const SceneOpenedEvent(
          sequence: 1,
          sceneKey: 'home',
          title: 'Home',
          commandId: 'a',
          shell: 'shell:dev/desk',
        ),
      );
      surface.apply(
        const SceneOpenedEvent(
          sequence: 2,
          sceneKey: 'countdown',
          title: 'Countdown',
          commandId: 'b',
          shell: 'shell:dev/desk',
        ),
      );

      await tester.pumpWidget(
        ShellSurfaceApp(controller: surface, shellName: 'desk'),
      );

      expect(find.text('shell:desk'), findsOneWidget);
      expect(find.byKey(const Key('shell_scene_list')), findsOneWidget);
      expect(find.byKey(const Key('scene_home')), findsOneWidget);
      expect(find.byKey(const Key('scene_countdown')), findsOneWidget);
      expect(find.text('home'), findsOneWidget);
      expect(find.text('Home'), findsOneWidget);
      expect(find.text('countdown'), findsOneWidget);
      expect(find.text('Countdown'), findsOneWidget);
    },
  );

  testWidgets(
    'ShellSurfaceApp projects SceneOpened from stream without restart',
    (tester) async {
      final surface = ShellSurfaceController();
      final events = StreamController<SceneOpenedEvent>();

      await tester.pumpWidget(
        ShellSurfaceApp(
          controller: surface,
          shellName: 'desk',
          events: events.stream,
        ),
      );

      expect(find.text('No scenes open'), findsOneWidget);

      events.add(
        const SceneOpenedEvent(
          sequence: 3,
          sceneKey: 'home',
          title: 'Home',
          commandId: 'c',
          shell: 'shell:dev/desk',
        ),
      );
      await tester.pump();

      expect(find.text('No scenes open'), findsNothing);
      expect(find.text('home'), findsOneWidget);
      expect(find.text('Home'), findsOneWidget);

      await events.close();
    },
  );
}
