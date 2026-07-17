class BlockAction {
  const BlockAction({
    required this.label,
    required this.contract,
    required this.inputJson,
  });

  factory BlockAction.fromJson(Map<String, dynamic> json) {
    return BlockAction(
      label: json['label']?.toString() ?? '',
      contract: json['contract']?.toString() ?? '',
      inputJson: json['inputJson']?.toString() ?? '',
    );
  }

  final String label;
  final String contract;
  final String inputJson;
}
