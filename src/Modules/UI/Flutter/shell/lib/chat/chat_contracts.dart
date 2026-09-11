import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';

typedef SendMessage = Future<ChatSendReceipt> Function(String text);
typedef SendVoice = Future<ChatSendReceipt> Function(
  List<int> audioBytes, {
  String fileName,
});
typedef OpenUrl = Future<void> Function(Uri url);
typedef CancelChatTurn = Future<void> Function({required String turnId});
typedef ReadChart = Future<ChatChartOffer?> Function(String name);
typedef ReadImageBytes = Future<Uint8List?> Function(String name);
typedef ReadSpreadsheet = Future<ChatSpreadsheetOffer?> Function(String name);
typedef ReadGraph = Future<ChatGraphOffer?> Function(String name);
typedef ReadSurface = Future<UiSurfaceState?> Function(String name);
typedef ReadActivityResults = Future<List<ChatTurnEvent>> Function(
  String activityId,
);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const onboardingDestinationIndex = 1;
const graphDestinationIndex = 2;
const activityDestinationIndex = 3;
const uiDestinationIndex = 4;
const windowingDestinationIndex = 5;

extension ChatTurnUiParts on ChatTurnEvent {
  List<UiPart> get uiParts => [
    for (final card in cards)
      ...switch (card.kind) {
        'chart' => [UiChartRefPart(name: card.name, caption: card.caption)],
        'image' => [UiImageRefPart(name: card.name, caption: card.caption)],
        'spreadsheet' => [
          UiSheetRefPart(name: card.name, caption: card.caption),
        ],
        'graph' => [UiGraphRefPart(name: card.name, caption: card.caption)],
        _ => const <UiPart>[],
      },
  ];
}
