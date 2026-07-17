class NeuronSnapshot {
  NeuronSnapshot({required this.revision, required this.stateJson});

  factory NeuronSnapshot.fromJson(Map<String, dynamic> json) {
    return NeuronSnapshot(
      revision: json['revision'] as int,
      stateJson: json['stateJson'] as String,
    );
  }

  final int revision;
  final String stateJson;
}

class NeuronDescription {
  NeuronDescription({
    required this.kind,
    required this.revision,
    required this.contracts,
  });

  factory NeuronDescription.fromJson(Map<String, dynamic> json) {
    return NeuronDescription(
      kind: json['kind'] as String,
      revision: json['revision'] as int,
      contracts: (json['contracts'] as List<dynamic>)
          .map((contract) => contract as String)
          .toList(),
    );
  }

  final String kind;
  final int revision;
  final List<String> contracts;
}

class FeedFrame {
  FeedFrame({required this.sequence, required this.record});

  factory FeedFrame.fromJson(Map<String, dynamic> json) {
    return FeedFrame(
      sequence: json['sequence'] as int,
      record: json['record'] as Map<String, dynamic>,
    );
  }

  final int sequence;
  final Map<String, dynamic> record;
}

class GatewayException implements Exception {
  GatewayException(this.code, this.detail);

  final String code;
  final String detail;

  @override
  String toString() => 'GatewayException($code, $detail)';
}
