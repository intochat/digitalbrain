import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

// Reproduces the real ui-gallery "inputs" hop: a ui:Screen containing a ui:Sidebar (full-height nav rail)
// plus a heading, many panels, and a button. The sidebar must not be stacked into the screen's vertical
// column (where it gets unbounded height and blanks the view), and the many panels must scroll.
Map<String, Object?> _node(String type, Map<String, Object?> props,
        [List<Object?> children = const []]) =>
    {'Type': type, 'Props': props, 'Children': children};

void main() {
  testWidgets('ui-gallery inputs hop renders sidebar + panels without a layout error',
      (tester) async {
    final data = <String, Object?>{
      'activeExperience': 'ui-gallery/ui-gallery',
      'surfaceId': 'inputs',
      'tree': _node('ui:Screen', const {}, [
        _node('ui:Sidebar', {
          'pack': 'ui-gallery',
          'experienceId': 'ui-gallery',
          'items': const [
            {'label': 'Inputs', 'eventName': 'inputs'},
            {'label': 'Display', 'eventName': 'display'},
            {'label': 'Feedback', 'eventName': 'feedback'},
          ],
        }),
        _node('ui:Heading', const {'text': 'Inputs'}),
        for (var i = 0; i < 8; i++)
          _node('ui:Panel', const {}, [
            _node('ui:Text', const {'text': 'TextField'}),
            _node('ui:TextField', const {'name': 'name', 'placeholder': 'Your name'}),
          ]),
        _node('ui:Button', const {
          'label': 'Next: Display',
          'pack': 'ui-gallery',
          'experienceId': 'ui-gallery',
          'eventName': 'display',
        }),
      ]),
    };

    await tester.pumpWidget(
      MaterialApp(
        builder: (_, child) =>
            FTheme(data: FThemes.neutral.dark.desktop, child: child!),
        home: Scaffold(
          body: ExperienceHopView(
            host: RfwRuntimeHost(),
            data: data,
            correlationId: 'inputs',
            onEvent: (name, args) {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    // "Inputs" renders both as the heading and as a sidebar nav item — proves the sidebar laid out too.
    expect(find.text('Inputs'), findsWidgets);
    expect(find.text('Display'), findsWidgets); // sidebar nav item
    expect(find.text('Next: Display'), findsOneWidget); // the trailing button is built (scrollable content)
  });
}
