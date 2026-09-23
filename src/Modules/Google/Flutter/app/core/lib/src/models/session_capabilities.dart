/// Server-owned capabilities the shell may not override locally.
class SessionCapabilities {
  const SessionCapabilities({required this.developerMode});

  factory SessionCapabilities.fromJson(Map<String, dynamic> json) =>
      SessionCapabilities(developerMode: json['developerMode'] == true);

  final bool developerMode;
}
