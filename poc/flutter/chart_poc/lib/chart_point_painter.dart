part of 'chart_plot.dart';

final class _ChartPointPainter extends CustomPainter {
  _ChartPointPainter(this.points);

  final List<ChartPointView> points;

  @override
  void paint(Canvas canvas, Size size) {
    if (points.isEmpty) {
      return;
    }

    final paint = Paint()
      ..color = const Color(0xff3f51b5)
      ..style = PaintingStyle.fill;
    final horizontalStep = size.width / (points.length + 1);
    final verticalStep = size.height / (points.length + 1);
    for (var index = 0; index < points.length; index++) {
      canvas.drawCircle(
        Offset(
          horizontalStep * (index + 1),
          size.height - verticalStep * (index + 1),
        ),
        7,
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _ChartPointPainter oldDelegate) =>
      oldDelegate.points != points;
}
