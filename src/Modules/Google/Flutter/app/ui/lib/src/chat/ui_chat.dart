import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../models/ui_part.dart';
import '../theme/ui_theme.dart';
import 'ui_copyable_message.dart';

/// The shared chat surface for full pages and embedded assistant panels.
///
/// Hosts own conversations, transport, and optional specialized builders. The
/// ui owns the chat presentation and theme in every workspace destination.
final class UiChat extends StatelessWidget {
  const UiChat({
    super.key,
    required this.chatController,
    required this.currentUserId,
    required this.resolveUser,
    this.builders,
    this.onMessageSend,
    this.onAttachmentTap,
    this.workspaceTheme = false,
  });

  final bool workspaceTheme;
  final ChatController chatController;
  final UserID currentUserId;
  final ResolveUserCallback resolveUser;
  final Builders? builders;
  final OnMessageSendCallback? onMessageSend;
  final OnAttachmentTapCallback? onAttachmentTap;

  @override
  Widget build(BuildContext context) => Chat(
    chatController: chatController,
    currentUserId: currentUserId,
    resolveUser: resolveUser,
    builders: _withCopy(builders),
    onMessageSend: onMessageSend,
    onAttachmentTap: onAttachmentTap,
    theme: workspaceTheme
        ? UiChatTheme.workspace(Theme.of(context))
        : Theme.of(context).brightness == Brightness.light
        ? UiChatTheme.light()
        : UiChatTheme.dark(),
  );
}

/// Wraps text, stream, and custom bubbles with selection + copy.
Builders _withCopy(Builders? host) {
  final base = host ?? const Builders();
  final text = base.textMessageBuilder;
  final stream = base.textStreamMessageBuilder;
  final custom = base.customMessageBuilder;
  var builders = base.copyWith(
    textMessageBuilder:
        (
          context,
          message,
          index, {
          required bool isSentByMe,
          MessageGroupStatus? groupStatus,
        }) {
          final child =
              text?.call(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              ) ??
              SimpleTextMessage(message: message, index: index);
          return UiCopyableMessage(copyText: (_) => message.text, child: child);
        },
  );
  if (stream != null) {
    builders = builders.copyWith(
      textStreamMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            return UiCopyableMessage(
              copyText: (context) =>
                  UiStreamCopy.streamText(context, message.streamId),
              child: stream(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              ),
            );
          },
    );
  }
  if (custom != null) {
    builders = builders.copyWith(
      customMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            final child = custom(
              context,
              message,
              index,
              isSentByMe: isSentByMe,
              groupStatus: groupStatus,
            );
            if (child is UiCopyableMessage) {
              return child;
            }
            final part = UiPart.tryParse(
              message.metadata == null
                  ? null
                  : Map<String, dynamic>.from(message.metadata!),
            );
            final copy = part?.copyText ?? '';
            if (copy.trim().isEmpty) {
              return child;
            }
            return UiCopyableMessage(copyText: (_) => copy, child: child);
          },
    );
  }
  return builders;
}
