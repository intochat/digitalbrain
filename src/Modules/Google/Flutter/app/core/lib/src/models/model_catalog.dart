class ChatModelOption {
  const ChatModelOption({
    this.id,
    required this.label,
    this.provider,
    this.model,
    this.available = true,
    this.capabilities = const [],
  });
  final String? id, provider, model;
  final String label;
  final bool available;
  final List<String> capabilities;
  factory ChatModelOption.fromJson(Map<String, dynamic> json) =>
      ChatModelOption(
        id: json['id'] as String?,
        label: json['label'] as String? ?? 'Automatic',
        provider: json['provider'] as String?,
        model: json['model'] as String?,
        available: json['available'] == true,
        capabilities: (json['capabilities'] as List? ?? [])
            .whereType<String>()
            .toList(),
      );
}

class ChatModelCatalog {
  const ChatModelCatalog({required this.automatic, required this.models});
  final ChatModelOption automatic;
  final List<ChatModelOption> models;
  factory ChatModelCatalog.fromJson(Map<String, dynamic> json) =>
      ChatModelCatalog(
        automatic: ChatModelOption.fromJson(
          Map<String, dynamic>.from(json['automatic'] as Map),
        ),
        models: (json['models'] as List? ?? [])
            .whereType<Map>()
            .map((v) => ChatModelOption.fromJson(Map<String, dynamic>.from(v)))
            .toList(),
      );
}
