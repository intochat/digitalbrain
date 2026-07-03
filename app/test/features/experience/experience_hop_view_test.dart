import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';

void main() {
  testWidgets('renders the hop body under the surfaceId semantics identifier', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: ExperienceHopView(
          host: host,
          data: const <String, Object?>{
            'activeExperience': 'travel/plan-trip',
            'source': 'import digitalbrain;\nwidget root = Text(text: "hotels-hop");',
          },
          correlationId: 'travel-hotels',
          rootWidget: 'root',
          onEvent: (_, _) {},
        ),
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('travel-hotels'), findsOneWidget);
    expect(find.text('hotels-hop'), findsOneWidget);
    handle.dispose();
  });
}
