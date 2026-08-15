import 'package:digitalbrain_flutter_shell/behaviors/behavior_demo_fixtures.dart';
import 'package:digitalbrain_flutter_shell/behaviors/behavior_view_model.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('offline controller surfaces two demo behaviors', () {
    final controller = BehaviorStudioController();
    expect(controller.showingDemoFixtures, isTrue);
    expect(controller.library, hasLength(2));
    expect(
      controller.library.map((item) => item.behaviorId),
      containsAll([
        BehaviorDemoFixtures.accountEnrichmentId,
        BehaviorDemoFixtures.inboxBriefId,
      ]),
    );
  });

  test('opening a demo behavior fills overview source and scenarios', () async {
    final controller = BehaviorStudioController();
    await controller.openBehavior(BehaviorDemoFixtures.accountEnrichmentId);
    expect(controller.view, BehaviorStudioView.overview);
    expect(controller.selected, isNotNull);
    expect(controller.selected!.programSource, contains('AccountEnrichmentProgram'));
    expect(controller.selected!.scenarios, isNotEmpty);
    expect(controller.selected!.bindings, isNotEmpty);
  });
}
