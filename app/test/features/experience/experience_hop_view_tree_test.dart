import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';

void main() {
  testWidgets('typed-tree hop renders ui:Text content under correlationId semantics', (tester) async {
    final handle = tester.ensureSemantics();

    final data = <String, Object?>{
      'activeExperience': 'onboarding/intro',
      'surfaceId': 'ask',
      'tree': <String, Object?>{
        'Type': 'ui:Screen',
        'Props': <String, Object?>{},
        'Children': [
          <String, Object?>{
            'Type': 'ui:Text',
            'Props': <String, Object?>{'text': "What's your name?"},
            'Children': <Object?>[],
          },
        ],
      },
    };

    await tester.pumpWidget(
      MaterialApp(
        builder: (_, w) => FTheme(
          data: FThemes.neutral.light.touch,
          child: w!,
        ),
        home: Scaffold(
          body: ExperienceHopView(
            host: RfwRuntimeHost(),
            data: data,
            correlationId: 'ask',
            onEvent: (name, args) {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text("What's your name?"), findsOneWidget);
    expect(find.bySemanticsIdentifier('ask'), findsOneWidget);

    handle.dispose();
  });
}
