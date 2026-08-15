import 'package:flutter_chat_core/flutter_chat_core.dart';

import '../models/kit_part.dart';

/// Builds [flutter_chat_core] messages from journal turns + kit parts.
///
/// Flyer Chat renders non-text UI via [CustomMessage] + `customMessageBuilder`
/// (see https://pub.dev/packages/flutter_chat_ui — CustomMessage / builders).
abstract final class KitMessageFactory {
  static const defaultAssistantUserId = 'assistant';
  static const defaultOwnerUserId = 'owner';

  /// One text bubble plus one [CustomMessage] per kit part on the turn.
  static List<Message> messagesForTurn({
    required int sequence,
    required bool fromUser,
    required String text,
    required DateTime createdAt,
    List<KitPart> parts = const [],
    String ownerUserId = defaultOwnerUserId,
    String assistantUserId = defaultAssistantUserId,
  }) {
    final authorId = fromUser ? ownerUserId : assistantUserId;
    final messages = <Message>[];

    if (text.trim().isNotEmpty) {
      messages.add(
        TextMessage(
          id: 'turn_${sequence}_text',
          authorId: authorId,
          createdAt: createdAt.toUtc(),
          text: text,
        ),
      );
    }

    for (var i = 0; i < parts.length; i++) {
      final part = parts[i];
      messages.add(
        CustomMessage(
          id: 'turn_${sequence}_${part.kind}_$i',
          authorId: authorId,
          createdAt: createdAt.toUtc(),
          metadata: part.toMetadata(),
        ),
      );
    }

    return messages;
  }

  static CustomMessage customMessageForPart({
    required String id,
    required String authorId,
    required DateTime createdAt,
    required KitPart part,
  }) {
    return CustomMessage(
      id: id,
      authorId: authorId,
      createdAt: createdAt.toUtc(),
      metadata: part.toMetadata(),
    );
  }
}
