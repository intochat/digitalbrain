import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/features/experience/experience_match.dart';

void main() {
  test('matches any experience hop when no target is given', () {
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, null), isTrue);
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, ''), isTrue);
  });

  test('matches only the targeted experience when a target is given', () {
    expect(experienceHopMatches({'activeExperience': 'travel/plan-trip'}, 'travel/plan-trip'), isTrue);
    expect(experienceHopMatches({'activeExperience': 'food/order'}, 'travel/plan-trip'), isFalse);
  });

  test('rejects non-experience surfaces', () {
    expect(experienceHopMatches(const {}, null), isFalse);
    expect(experienceHopMatches(const {'activeExperience': ''}, null), isFalse);
  });
}
