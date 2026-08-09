part of 'chart_poc_app.dart';

final class _EmptyChartProjection implements ChartProjection {
  const _EmptyChartProjection();

  @override
  Future<List<ChartPointView>> loadPoints() async => const [];
}
