import 'package:flutter/material.dart';

import 'chart_plot.dart';
import 'chart_projection.dart';

part 'chart_screen_state.dart';

class ChartScreen extends StatefulWidget {
  const ChartScreen({required this.projection, super.key});

  final ChartProjection projection;

  @override
  State<ChartScreen> createState() => _ChartScreenState();
}
