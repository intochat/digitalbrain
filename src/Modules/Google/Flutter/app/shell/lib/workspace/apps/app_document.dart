Map<String, dynamic> appMap(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};
List<String> _strings(dynamic value) => (value as List? ?? []).cast<String>();

class AppDocument {
  const AppDocument({
    this.preamble = '',
    required this.behaviors,
    required this.scenarios,
  });
  final String preamble;
  final List<AppBehavior> behaviors;
  final List<AppScenario> scenarios;
  factory AppDocument.fromJson(Map<String, dynamic> json) {
    if (json['version'] != 1) {
      throw const FormatException('Unsupported authoring document');
    }
    return AppDocument(
      preamble: json['preamble'] as String? ?? '',
      behaviors: (json['behaviors'] as List? ?? [])
          .map((x) => AppBehavior.fromJson(appMap(x)))
          .toList(),
      scenarios: (json['scenarios'] as List? ?? [])
          .map((x) => AppScenario.fromJson(appMap(x)))
          .toList(),
    );
  }
  Map<String, dynamic> toJson() => {
    'version': 1,
    'preamble': preamble,
    'behaviors': behaviors.map((b) => b.toJson()).toList(),
    'scenarios': scenarios.map((s) => s.toJson()).toList(),
  };
  AppDocument withBehaviors(List<AppBehavior> values) =>
      AppDocument(preamble: preamble, behaviors: values, scenarios: scenarios);
}

class AppBehavior {
  const AppBehavior({
    required this.id,
    required this.title,
    required this.description,
    this.sourcePaths = const [],
    this.scenarioIds = const [],
  });
  final String id, title, description;
  final List<String> sourcePaths, scenarioIds;
  factory AppBehavior.fromJson(Map<String, dynamic> j) => AppBehavior(
    id: j['id'] as String,
    title: j['title'] as String,
    description: j['description'] as String,
    sourcePaths: _strings(j['sourcePaths']),
    scenarioIds: _strings(j['scenarioIds']),
  );
  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'description': description,
    'sourcePaths': sourcePaths,
    'scenarioIds': scenarioIds,
  };
  AppBehavior duplicate(String newId) =>
      AppBehavior(id: newId, title: '$title copy', description: description);
  AppBehavior edit(String title, String description, List<String> scenarios) =>
      AppBehavior(
        id: id,
        title: title,
        description: description,
        sourcePaths: sourcePaths,
        scenarioIds: scenarios,
      );
}

class AppScenario {
  const AppScenario({
    required this.id,
    required this.name,
    required this.body,
    this.isLive = false,
  });
  final String id, name, body;
  final bool isLive;
  factory AppScenario.fromJson(Map<String, dynamic> j) => AppScenario(
    id: j['id'] as String,
    name: j['name'] as String,
    body: j['body'] as String,
    isLive: j['isLive'] == true,
  );
  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'body': body,
    'isLive': isLive,
  };
}
