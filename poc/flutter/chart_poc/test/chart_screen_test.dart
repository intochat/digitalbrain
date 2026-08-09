import 'package:chart_poc/chart_projection.dart';
import 'package:chart_poc/chart_screen.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/widgets.dart';

void main() {
  testWidgets('renders the persisted chart point', (tester) async {
    final projection = FakeChartProjection(const [
      ChartPointView(sourcePostId: 'post-1', ordinal: 1),
    ]);

    await tester.pumpWidget(ChartScreen(projection: projection));
    await tester.pump();

    expect(find.byType(CustomPaint), findsOneWidget);
    expect(find.text('1'), findsOneWidget);
  });
}

final class FakeChartProjection implements ChartProjection {
  FakeChartProjection(this.points);

  final List<ChartPointView> points;

  @override
  Future<List<ChartPointView>> loadPoints() async => points;
}
