import 'app_document.dart';

class ScenarioVerdict {
  const ScenarioVerdict(this.name, this.passed, this.message);
  final String name, message;
  final bool passed;
}

class VerificationRun {
  const VerificationRun(this.exitCode, this.scenarios);
  final int? exitCode;
  final List<ScenarioVerdict> scenarios;
  factory VerificationRun.fromJson(Map<String, dynamic> j) => VerificationRun(
    j['exitCode'] as int?,
    (j['scenarios'] as List? ?? []).map((x) {
      final m = appMap(x);
      return ScenarioVerdict(
        '${m['name']}',
        m['passed'] == true,
        '${m['message'] ?? ''}',
      );
    }).toList(),
  );
  ScenarioVerdict? verdict(String name) {
    final found = scenarios.where((s) => s.name == name).toList();
    return found.length == 1 ? found.single : null;
  }

  bool get green =>
      exitCode == 0 && scenarios.isNotEmpty && scenarios.every((s) => s.passed);
}

String scenarioStatus(
  AppScenario s,
  List<AppScenario> all,
  VerificationRun? run,
  bool current,
) {
  if (s.isLive) return 'Live documentation';
  if (all.where((x) => x.name == s.name).length != 1) return 'Ambiguous name';
  if (!current && run != null) return 'Needs checking';
  final verdict = run?.verdict(s.name);
  if (verdict == null) return 'Not run';
  return verdict.passed ? 'Passed' : 'Failed';
}

String behaviorStatus(
  List<AppScenario> linked,
  List<AppScenario> all,
  VerificationRun? run,
  bool current,
) {
  if (linked.isEmpty) return 'No checks';
  if (linked.every((s) => s.isLive)) return 'Live documentation';
  final statuses = linked
      .where((s) => !s.isLive)
      .map((s) => scenarioStatus(s, all, run, current))
      .toList();
  if (statuses.contains('Needs checking')) return 'Needs checking';
  if (statuses.contains('Failed') || current && run != null && !run.green) {
    return 'Checks failed';
  }
  if (statuses.every((s) => s == 'Passed')) return 'Checks passed';
  return 'Not run';
}
