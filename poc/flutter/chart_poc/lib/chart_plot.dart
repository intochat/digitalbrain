import 'package:flutter/material.dart';

import 'chart_point_view.dart';

part 'chart_point_painter.dart';

class ChartPlot extends StatelessWidget {
  const ChartPlot({required this.points, super.key});

  final List<ChartPointView> points;

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 180,
    width: double.infinity,
    child: CustomPaint(painter: _ChartPointPainter(points)),
  );
}
