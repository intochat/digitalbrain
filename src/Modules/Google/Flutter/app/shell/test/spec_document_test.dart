import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/spec_document.dart';

void main() {
  test('legacy content round trips including fences and malformed headings', () {
    const raw =
        'Intro\r\n```\n## Scenario: not a scenario\n```\n## Scenario: First\nBody\n## Scenario:\nkept\n ## Scenario: Live (live)\nLive body';
    final parsed = parseSpec(raw);
    expect(parsed.original, raw);
    expect(parsed.scenarios.map((s) => s.name), ['First', 'Live']);
    expect(parsed.scenarios.first.body, contains('## Scenario:'));
    expect(parsed.scenarios.last.isLive, true);
    expect(parseSpec('').scenarios, isEmpty);
  });
}
