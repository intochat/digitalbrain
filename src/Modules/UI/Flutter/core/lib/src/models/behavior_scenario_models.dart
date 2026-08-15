final class BehaviorScenario {
  const BehaviorScenario({
    required this.scenarioId,
    required this.title,
    required this.bindingKey,
    this.passed,
    this.detail,
  });

  final String scenarioId;
  final String title;
  final String bindingKey;
  final bool? passed;
  final String? detail;

  factory BehaviorScenario.fromJson(Map<String, Object?> json) {
    return BehaviorScenario(
      scenarioId: json['scenarioId'] as String,
      title: json['title'] as String,
      bindingKey: json['bindingKey'] as String,
      passed: json['passed'] as bool?,
      detail: json['detail'] as String?,
    );
  }
}

final class BehaviorBinding {
  const BehaviorBinding({
    required this.bindingId,
    required this.sourceModule,
    required this.sourceSynapse,
    required this.targetCase,
    required this.contractVersion,
    required this.enabled,
    required this.configurationHint,
  });

  final String bindingId;
  final String sourceModule;
  final String sourceSynapse;
  final String targetCase;
  final String contractVersion;
  final bool enabled;
  final String configurationHint;

  factory BehaviorBinding.fromJson(Map<String, Object?> json) {
    return BehaviorBinding(
      bindingId: json['bindingId'] as String,
      sourceModule: json['sourceModule'] as String,
      sourceSynapse: json['sourceSynapse'] as String,
      targetCase: json['targetCase'] as String,
      contractVersion: json['contractVersion'] as String,
      enabled: json['enabled'] as bool? ?? true,
      configurationHint: json['configurationHint'] as String? ?? 'opaque',
    );
  }
}
