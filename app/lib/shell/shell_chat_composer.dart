part of 'forui_app_shell.dart';

@visibleForTesting
const shellComposerAttachButtonKey = Key('shell-composer-attach');

@visibleForTesting
const shellComposerVoiceButtonKey = Key('shell-composer-voice');

@visibleForTesting
void appendTranscriptToComposer(
  TextEditingController controller,
  String transcript,
) {
  final text = transcript.trim();
  if (text.isEmpty) return;

  final existing = controller.text.trim();
  controller.text = existing.isEmpty ? text : '$existing $text';
  controller.selection = TextSelection.collapsed(
    offset: controller.text.length,
  );
}

@visibleForTesting
class ShellChatComposer extends StatelessWidget {
  const ShellChatComposer({
    super.key,
    required this.controller,
    required this.sending,
    required this.onSend,
    required this.onAttachFiles,
    this.voiceInput,
    this.status,
  });

  final TextEditingController controller;
  final bool sending;
  final VoidCallback? onSend;
  final VoidCallback? onAttachFiles;
  final Widget? voiceInput;
  final String? status;

  @override
  Widget build(BuildContext context) {
    final t = FTheme.of(context);
    final statusText = status?.trim();

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: t.colors.border, width: 0.5)),
        color: t.colors.background,
        borderRadius: const BorderRadius.vertical(bottom: Radius.circular(12)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (statusText != null && statusText.isNotEmpty) ...[
            Text(
              statusText,
              style: t.typography.sm.copyWith(color: t.colors.mutedForeground),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
            const SizedBox(height: 8),
          ],
          Row(
            children: [
              Tooltip(
                message: 'Attach file',
                child: FButton(
                  key: shellComposerAttachButtonKey,
                  onPress: onAttachFiles,
                  child: const Icon(Icons.attach_file),
                ),
              ),
              const SizedBox(width: 8),
              if (voiceInput != null) ...[
                KeyedSubtree(
                  key: shellComposerVoiceButtonKey,
                  child: voiceInput!,
                ),
                const SizedBox(width: 8),
              ],
              Expanded(
                child: FTextField(
                  control: FTextFieldControl.managed(controller: controller),
                  hint: 'Ask INO...',
                  onSubmit: (_) => onSend?.call(),
                ),
              ),
              const SizedBox(width: 8),
              Tooltip(
                message: 'Send',
                child: FButton(
                  onPress: sending ? null : onSend,
                  child: const Icon(Icons.send),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
