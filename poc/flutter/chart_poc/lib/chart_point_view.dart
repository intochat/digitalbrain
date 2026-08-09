class ChartPointView {
  const ChartPointView({
    required this.sourcePostId,
    required this.ordinal,
    this.occurredAt,
  });

  factory ChartPointView.fromJson(Map<String, Object?> json) {
    final sourcePostId = json['sourcePostId'];
    final ordinal = json['ordinal'];
    final occurredAt = json['occurredAt'];
    if (sourcePostId is! String || ordinal is! int) {
      throw const FormatException('Chart point projection is malformed.');
    }

    return ChartPointView(
      sourcePostId: sourcePostId,
      ordinal: ordinal,
      occurredAt: occurredAt is String ? DateTime.parse(occurredAt) : null,
    );
  }

  final String sourcePostId;
  final int ordinal;
  final DateTime? occurredAt;
}
