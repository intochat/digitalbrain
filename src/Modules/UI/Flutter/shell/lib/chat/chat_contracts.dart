import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);
typedef StreamVoice =
    Stream<ChatDelta> Function(List<int> audioBytes, {String fileName});
typedef OpenUrl = Future<void> Function(Uri url);
typedef ActivateChatButton =
    Future<void> Function({
      required String offerCommandId,
      required String buttonId,
      required String action,
    });
typedef ReadChart = Future<ChatChartOffer?> Function(String name);
typedef ReadImageBytes = Future<Uint8List?> Function(String name);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const behaviorsDestinationIndex = 2;
const kitDestinationIndex = 3;

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
    for (final card in cards)
      ...switch (card.kind) {
        'chart' => [KitChartRefPart(name: card.name, caption: card.caption)],
        'image' => [KitImageRefPart(name: card.name, caption: card.caption)],
        _ => const <KitPart>[],
      },
  ];
}
