import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../theme/ui_theme.dart';
import 'ui_chat_builders.dart';

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
    builders: UiChatBuilders.withCopy(builders),
    onMessageSend: onMessageSend,
    onAttachmentTap: onAttachmentTap,
    theme: workspaceTheme
        ? UiChatTheme.workspace(Theme.of(context))
        : Theme.of(context).brightness == Brightness.light
        ? UiChatTheme.light()
        : UiChatTheme.dark(),
  );
}
