final class BehaviorTestReport {
  const BehaviorTestReport({
    required this.allGreen,
    required this.scenarios,
    required this.failures,
  });

  factory BehaviorTestReport.fromJson(Map<String, Object?> json) =>
      BehaviorTestReport(
        allGreen: json['allGreen'] as bool? ?? false,
        scenarios: (json['scenarios'] as num?)?.toInt() ?? 0,
        failures: (json['failures'] as List<Object?>? ?? const [])
            .cast<String>(),
      );

  final bool allGreen;
  final int scenarios;
  final List<String> failures;
}

final class BehaviorDiagnostic {
  const BehaviorDiagnostic({
    required this.code,
    required this.message,
    required this.line,
    required this.severity,
  });

  factory BehaviorDiagnostic.fromJson(Map<String, Object?> json) =>
      BehaviorDiagnostic(
        code: json['code'] as String? ?? '',
        message: json['message'] as String? ?? '',
        line: (json['line'] as num?)?.toInt() ?? 0,
        severity: json['severity']?.toString() ?? '0',
      );

  final String code;
  final String message;
  final int line;
  final String severity;
}

final class BehaviorSummary {
  const BehaviorSummary({
    required this.name,
    required this.title,
    required this.source,
    required this.active,
    required this.diagnostics,
    this.lastTest,
  });

  factory BehaviorSummary.fromJson(Map<String, Object?> json) =>
      BehaviorSummary(
        name: json['name'] as String,
        title: json['title'] as String,
        source: json['source'] as String,
        active: json['active'] as bool? ?? false,
        lastTest: json['lastTest'] is Map<String, Object?>
            ? BehaviorTestReport.fromJson(
                json['lastTest']! as Map<String, Object?>,
              )
            : null,
        diagnostics: (json['diagnostics'] as List<Object?>? ?? const [])
            .cast<Map<String, Object?>>()
            .map(BehaviorDiagnostic.fromJson)
            .toList(),
      );

  final String name;
  final String title;
  final String source;
  final bool active;
  final BehaviorTestReport? lastTest;
  final List<BehaviorDiagnostic> diagnostics;

  BehaviorSummary copyWith({
    String? source,
    bool? active,
    BehaviorTestReport? lastTest,
    List<BehaviorDiagnostic>? diagnostics,
  }) => BehaviorSummary(
    name: name,
    title: title,
    source: source ?? this.source,
    active: active ?? this.active,
    lastTest: lastTest ?? this.lastTest,
    diagnostics: diagnostics ?? this.diagnostics,
  );
}

final class BehaviorStepSuggestion {
  const BehaviorStepSuggestion({
    required this.keyword,
    required this.template,
    required this.description,
  });

  factory BehaviorStepSuggestion.fromJson(Map<String, Object?> json) =>
      BehaviorStepSuggestion(
        keyword: json['keyword'] as String,
        template: json['template'] as String,
        description: json['description'] as String,
      );

  final String keyword;
  final String template;
  final String description;
}

final class BehaviorGeneration {
  const BehaviorGeneration({
    required this.source,
    required this.model,
    required this.success,
    required this.diagnostics,
  });

  factory BehaviorGeneration.fromJson(Map<String, Object?> json) {
    final compilation = json['compilation']! as Map<String, Object?>;
    return BehaviorGeneration(
      source: json['source'] as String,
      model: json['model'] as String,
      success: compilation['success'] as bool? ?? false,
      diagnostics: (compilation['diagnostics'] as List<Object?>? ?? const [])
          .cast<Map<String, Object?>>()
          .map(BehaviorDiagnostic.fromJson)
          .toList(),
    );
  }

  final String source;
  final String model;
  final bool success;
  final List<BehaviorDiagnostic> diagnostics;
}
