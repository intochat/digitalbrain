import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('the catalog lists the eight foundational capabilities in rail order', () {
    expect(
      OnboardingCatalog.capabilities.map((item) => item.id).toList(),
      [
        'fire',
        'handle',
        'synapse',
        'broadcast',
        'subscribe',
        'journal',
        'entity',
        'module',
      ],
    );
  });

  test('a handled fire ends with a solid synapse on the source', () {
    final last = OnboardingCatalog.synapse.frames.last;
    final edge = last.edges.single;
    expect(edge.sourceId, 'elon');
    expect(edge.targetId, 'alice');
    expect(edge.dotted, isFalse);
    expect(last.highlightEdgeId, 'elon-alice');
  });

  test('broadcast never pulses the emitter', () {
    for (final frame in OnboardingCatalog.broadcast.frames) {
      expect(frame.pulse?.toId, isNot('elon'));
      expect(frame.pulse?.fromId ?? 'elon', 'elon');
    }
  });

  test('subscribe to elon does not pulse bob', () {
    final last = OnboardingCatalog.subscribe.frames.last;
    expect(last.pulse?.toId, 'alice');
    final bob = last.nodes.singleWhere((node) => node.id == 'bob');
    expect(bob.dimmed, isTrue);
  });

  test('entity profile is never a pulse target', () {
    for (final frame in OnboardingCatalog.entity.frames) {
      expect(frame.pulse?.toId, isNot('profile'));
      expect(
        frame.nodes.any((node) => node.kind == GraphNodeKind.entity),
        isTrue,
      );
    }
  });

  test('module lesson fires at IAspire, not an Orleans node', () {
    final last = OnboardingCatalog.module.frames.last;
    expect(last.pulse?.toId, 'aspire');
    expect(last.nodes.any((node) => node.id.toLowerCase().contains('orleans')), isFalse);
    expect(last.nodes.singleWhere((node) => node.id == 'aspire').kind, GraphNodeKind.module);
  });

  test('a player with animations off stays on the completed frame', () {
    final player = OnboardingLessonPlayer(animate: false);
    expect(player.frameIndex, OnboardingCatalog.fire.frames.length - 1);
    player.select('synapse');
    expect(player.capability.id, 'synapse');
    expect(player.frameIndex, OnboardingCatalog.synapse.frames.length - 1);
    player.dispose();
  });
}
