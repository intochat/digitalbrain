part of 'chart_screen.dart';

final class _ChartScreenState extends State<ChartScreen> {
  late Future<List<ChartPointView>> _points;

  @override
  void initState() {
    super.initState();
    _points = widget.projection.loadPoints();
  }

  @override
  void didUpdateWidget(covariant ChartScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.projection != widget.projection) {
      _points = widget.projection.loadPoints();
    }
  }

  @override
  Widget build(BuildContext context) => Directionality(
    textDirection: TextDirection.ltr,
    child: FutureBuilder<List<ChartPointView>>(
      future: _points,
      builder: (context, snapshot) {
        if (snapshot.hasError) {
          return const Center(child: Text('Chart unavailable'));
        }
        if (!snapshot.hasData) {
          return const Center(child: Text('Loading chart'));
        }

        final points = snapshot.data!;
        return Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('Trusted chart'),
              const SizedBox(height: 16),
              ChartPlot(points: points),
              const SizedBox(height: 16),
              Wrap(
                spacing: 12,
                children: [
                  for (final point in points) Text('${point.ordinal}'),
                ],
              ),
            ],
          ),
        );
      },
    ),
  );
}
