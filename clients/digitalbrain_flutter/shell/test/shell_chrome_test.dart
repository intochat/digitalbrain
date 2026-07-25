import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/shell_chrome.dart';
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

  testWidgets(
    'ShellSurfaceApp projects multi-event SSE stream without restart or rebuild of host',
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

      Future<void> project(SceneOpenedEvent event) async {
        events.add(event);
        await tester.idle();
        await tester.pump();
      }

      final homeElement = tester.element(find.byType(ShellSurfaceHome));
      expect(find.text('No scenes open'), findsOneWidget);

      await project(
        const SceneOpenedEvent(
          sequence: 3,
          sceneKey: 'alpha',
          title: 'Alpha one',
          commandId: 'c',
          shell: 'shell:dev/desk',
        ),
      );

      expect(find.byKey(const Key('scene_alpha')), findsOneWidget);
      expect(find.text('Alpha one'), findsOneWidget);
      expect(surface.scenes.map((s) => s.sceneKey), ['alpha']);
      expect(
        identical(homeElement, tester.element(find.byType(ShellSurfaceHome))),
        isTrue,
      );

      await project(
        const SceneOpenedEvent(
          sequence: 5,
          sceneKey: 'beta',
          title: 'Beta one',
          commandId: 'd',
          shell: 'shell:dev/desk',
        ),
      );

      expect(find.byKey(const Key('scene_alpha')), findsOneWidget);
      expect(find.byKey(const Key('scene_beta')), findsOneWidget);
      expect(find.text('Beta one'), findsOneWidget);
      expect(find.text('seq 5'), findsOneWidget);
      expect(surface.scenes.map((s) => s.sceneKey), ['alpha', 'beta']);
      expect(surface.latest?.sceneKey, 'beta');
      expect(
        identical(homeElement, tester.element(find.byType(ShellSurfaceHome))),
        isTrue,
      );

      await project(
        const SceneOpenedEvent(
          sequence: 7,
          sceneKey: 'alpha',
          title: 'Alpha two',
          commandId: 'e',
          shell: 'shell:dev/desk',
        ),
      );

      expect(surface.latest?.title, 'Alpha two');
      expect(surface.scenes.map((s) => '${s.sceneKey}:${s.title}'), [
        'alpha:Alpha two',
        'beta:Beta one',
      ]);
      expect(find.byKey(const Key('scene_alpha')), findsOneWidget);
      expect(find.byKey(const Key('scene_beta')), findsOneWidget);
      expect(find.text('Alpha one'), findsNothing);
      expect(find.text('Alpha two'), findsOneWidget);
      expect(find.text('seq 7'), findsOneWidget);
      expect(surface.scenes, hasLength(2));
      expect(
        identical(homeElement, tester.element(find.byType(ShellSurfaceHome))),
        isTrue,
      );

      await events.close();
    },
  );

  testWidgets(
    'ShellSurfaceApp projects parsed multi-frame SSE into chrome without restart',
    (tester) async {
      const frames = '''
: connected

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"c","shell":"shell:dev/desk"}

id: 4
event: noise
data: {"sequence":4,"sceneKey":"ignore","title":"Ignore","commandId":"n","shell":"shell:dev/desk"}

id: 5
event: scene-opened
data: {"sequence":5,"sceneKey":"countdown","title":"Countdown","commandId":"d","shell":"shell:dev/desk"}

''';

      final surface = ShellSurfaceController();
      final events = StreamController<SceneOpenedEvent>();

      await tester.pumpWidget(
        ShellSurfaceApp(
          controller: surface,
          shellName: 'desk',
          events: events.stream,
        ),
      );

      final homeElement = tester.element(find.byType(ShellSurfaceHome));
      final parser = SseSceneOpenedParser();
      Future<void> projectParsed(SceneOpenedEvent event) async {
        events.add(event);
        await tester.idle();
        await tester.pump();
      }

      for (final line in frames.split('\n')) {
        for (final event in parser.addLine(line)) {
          await projectParsed(event);
        }
      }
      for (final event in parser.flush()) {
        await projectParsed(event);
      }

      expect(find.byKey(const Key('scene_home')), findsOneWidget);
      expect(find.byKey(const Key('scene_countdown')), findsOneWidget);
      expect(find.text('Home'), findsOneWidget);
      expect(find.text('Countdown'), findsOneWidget);
      expect(find.text('Ignore'), findsNothing);
      expect(surface.scenes.map((s) => '${s.sceneKey}:${s.sequence}'), [
        'home:3',
        'countdown:5',
      ]);
      expect(
        identical(homeElement, tester.element(find.byType(ShellSurfaceHome))),
        isTrue,
      );

      await events.close();
    },
  );
}
