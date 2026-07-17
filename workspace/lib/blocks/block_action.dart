class BlockAction {
  const BlockAction({
    required this.label,
    required this.contract,
    required this.inputJson,
    this.target = '',
  });

  factory BlockAction.fromJson(Map<String, dynamic> json) {
    return BlockAction(
      label: json['label']?.toString() ?? '',
      contract: json['contract']?.toString() ?? '',
      target: json['target']?.toString() ?? '',
      inputJson: json['inputJson']?.toString() ?? '',
    );
  }

  final String label;
  final String contract;
  final String target;
  final String inputJson;
}
