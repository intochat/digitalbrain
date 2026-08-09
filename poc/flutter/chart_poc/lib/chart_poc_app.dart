import 'package:flutter/material.dart';

import 'chart_projection.dart';
import 'chart_screen.dart';

part 'empty_chart_projection.dart';

class ChartPocApp extends StatelessWidget {
  const ChartPocApp({this.projection, super.key});

  final ChartProjection? projection;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'DigitalBrain chart POC',
    theme: ThemeData(colorSchemeSeed: const Color(0xff3f51b5)),
    home: ChartScreen(projection: projection ?? const _EmptyChartProjection()),
  );
}
