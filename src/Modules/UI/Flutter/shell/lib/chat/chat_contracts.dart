import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);
typedef LoadTopology = Future<BrainTopologySnapshot> Function();
typedef OpenUrl = Future<void> Function(Uri url);
typedef ActivateChatButton = Future<void> Function({
  required String offerCommandId,
  required String buttonId,
  required String action,
});

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const brainDestinationIndex = 2;
const behaviorsDestinationIndex = 3;
const kitDestinationIndex = 4;
const windowingDestinationIndex = 5;

typedef LoadBehaviors = Future<BehaviorLibraryDocument> Function();
typedef OpenBehavior = Future<BehaviorDocument> Function(String behaviorId);

extension ChatTurnKitParts on ChatTurnEvent {
  List<KitPart> get kitParts => [
        for (final button in buttons)
          KitButtonPart(
            buttonId: button.buttonId,
            label: button.label,
            action: button.action,
            offerCommandId: commandId,
          ),
        for (final chart in charts)
          KitChartPart(
            title: chart.title,
            points: [
              for (final point in chart.points)
                KitChartPoint(label: point.label, value: point.value),
            ],
            chartKind: chart.chartKind,
          ),
        for (final timer in timers)
          KitTimerPart(label: timer.label, dueAt: timer.dueAt),
      ];
}
