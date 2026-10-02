import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_document.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_verification_summary.dart';

void main() {
  const scenario = AppScenario(id: 's', name: 'S', body: 'Body');
  test('checks never pass stale, absent, ambiguous or unsuccessful runs', () {
    final run = VerificationRun.fromJson({
      'exitCode': 0,
      'scenarios': [
        {'name': 'S', 'passed': true},
      ],
    });
    expect(scenarioStatus(scenario, [scenario], run, true), 'Passed');
    expect(scenarioStatus(scenario, [scenario], run, false), 'Needs checking');
    expect(
      scenarioStatus(scenario, [scenario, scenario], run, true),
      'Ambiguous name',
    );
    expect(behaviorStatus([], [scenario], run, true), 'No checks');
    expect(
      behaviorStatus(
        [scenario],
        [scenario],
        VerificationRun.fromJson({
          'exitCode': 1,
          'scenarios': [
            {'name': 'S', 'passed': true},
          ],
        }),
        true,
      ),
      'Checks failed',
    );
    expect(scenarioStatus(scenario, [scenario], null, true), 'Not run');
  });
}
