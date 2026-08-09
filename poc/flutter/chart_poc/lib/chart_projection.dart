import 'chart_point_view.dart';

export 'chart_point_view.dart';

abstract interface class ChartProjection {
  Future<List<ChartPointView>> loadPoints();
}
