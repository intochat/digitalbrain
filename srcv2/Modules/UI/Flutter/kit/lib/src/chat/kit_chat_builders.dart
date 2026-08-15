import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

import '../components/button/kit_button.dart';
import '../components/card/kit_card.dart';
import '../components/chart/kit_chart.dart';
import '../components/clock/kit_clock.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';

typedef KitButtonPressed = void Function(KitButtonPart part);

/// Flyer Chat [Builders] helpers for DigitalBrain kit components.
///
/// Official extension point: `Builders.customMessageBuilder` receives
/// [CustomMessage]; payload lives in `message.metadata` (kind-discriminated).
/// Docs: https://pub.dev/packages/flutter_chat_ui
abstract final class KitChatBuilders {
  static Widget customMessageBuilder(
    BuildContext context,
    CustomMessage message,
    int index, {
    required bool isSentByMe,
    MessageGroupStatus? groupStatus,
    KitButtonPressed? onButtonPressed,
  }) {
    final part = KitPart.tryParse(
      message.metadata == null
          ? null
          : Map<String, dynamic>.from(message.metadata!),
    );

    if (part == null) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 4),
        child: Text(
          'Unsupported kit message',
          style: KitType.bodyMuted,
          key: Key('kit_custom_unsupported'),
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: switch (part) {
        KitButtonPart(:final buttonId) => KitButton(
            key: Key('chat_kit_button_$buttonId'),
            part: part,
            dense: true,
            onPressed: onButtonPressed,
          ),
        KitChartPart() => KitChart(part: part, height: 180),
        KitCardPart() => KitCard(part: part),
        KitTimerPart() => KitClock(part: part),
      },
    );
  }

  /// Drop-in partial [Builders] for chat surfaces that only need kit customs.
  static Builders kitCustoms({KitButtonPressed? onButtonPressed}) {
    return Builders(
      customMessageBuilder: (
        context,
        message,
        index, {
        required bool isSentByMe,
        MessageGroupStatus? groupStatus,
      }) =>
          customMessageBuilder(
            context,
            message,
            index,
            isSentByMe: isSentByMe,
            groupStatus: groupStatus,
            onButtonPressed: onButtonPressed,
          ),
    );
  }
}
