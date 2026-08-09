import 'package:chart_poc/chart_poc_app.dart';
import 'package:chart_poc/chart_plot.dart';
import 'package:chart_poc/chart_projection_http_client.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'poc_host_fixture.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('renders points created by the approved module', (tester) async {
    final host = await PocHostFixture.startApprovedElonCandidate();
    addTearDown(host.disposeAndVerifyDeleted);
    await host.fireTrustedSocialPost(author: 'elonmusk', postId: 'post-1');

    final projection = ChartProjectionHttpClient(
      baseUri: host.baseUri,
      chartId: 'elon-chart',
      opaqueSessionToken: host.ownerSessionToken,
    );
    await tester.pumpWidget(ChartPocApp(projection: projection));
    await tester.pumpAndSettle();

    expect(
      find.descendant(
        of: find.byType(ChartPlot),
        matching: find.byType(CustomPaint),
      ),
      findsOneWidget,
    );
    expect(find.text('1'), findsOneWidget);
  });

  testWidgets('cleans up after a malformed post-readiness record', (_) async {
    final host = await PocHostFixture.startApprovedElonCandidate(
      emitMalformedPostReadinessRecord: true,
    );
    addTearDown(host.disposeAndVerifyDeleted);

    await expectLater(
      host.fireTrustedSocialPost(author: 'elonmusk', postId: 'post-malformed'),
      throwsA(isA<StateError>()),
    );
    await expectLater(
      host.disposeAndVerifyDeleted(),
      throwsA(isA<StateError>()),
    );
  });
}
