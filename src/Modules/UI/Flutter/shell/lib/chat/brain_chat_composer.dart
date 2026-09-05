import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';
import 'package:provider/provider.dart';

import '../brain_theme.dart';

final class VoiceComposerController extends ChangeNotifier {
  bool recording = false;
  bool busy = false;
  final draft = TextEditingController();
  final focus = FocusNode();

  void update({bool? recording, bool? busy}) {
    final nextRecording = recording ?? this.recording;
    final nextBusy = busy ?? this.busy;
    if (nextRecording == this.recording && nextBusy == this.busy) {
      return;
    }
    this.recording = nextRecording;
    this.busy = nextBusy;
    notifyListeners();
  }

  @override
  void dispose() {
    draft.dispose();
    focus.dispose();
    super.dispose();
  }
}

final class BrainChatComposer extends StatefulWidget {
  const BrainChatComposer({
    super.key,
    required this.canVoice,
    required this.onVoiceTap,
  });

  final bool canVoice;
  final VoidCallback onVoiceTap;

  @override
  State<BrainChatComposer> createState() => _BrainChatComposerState();
}

final class _BrainChatComposerState extends State<BrainChatComposer> {
  final _key = GlobalKey();
  late final TextEditingController _text;
  late final ValueNotifier<bool> _hasText;

  @override
  void initState() {
    super.initState();
    _text = context.read<VoiceComposerController>().draft;
    _hasText = ValueNotifier(_text.text.trim().isNotEmpty);
    _text.addListener(_onText);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void didUpdateWidget(covariant BrainChatComposer oldWidget) {
    super.didUpdateWidget(oldWidget);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void dispose() {
    _text.removeListener(_onText);
    _hasText.dispose();
    super.dispose();
  }

  void _onText() => _hasText.value = _text.text.trim().isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final onSend = context.read<OnMessageSendCallback?>();
    final onAttach = context.read<OnAttachmentTapCallback?>();
    final theme = context.select(
      (ChatTheme t) => (
        bodyMedium: t.typography.bodyMedium,
        onSurface: t.colors.onSurface,
        surfaceHigh: t.colors.surfaceContainerHigh,
        surfaceLow: t.colors.surfaceContainerLow,
      ),
    );
    final bottomSafe = MediaQuery.of(context).padding.bottom;
    final muted = theme.onSurface.withValues(alpha: 0.5);

    return Positioned(
      left: 0,
      right: 0,
      bottom: 0,
      child: Container(
        key: _key,
        color: theme.surfaceLow,
        padding: EdgeInsets.fromLTRB(8, 8, 8, 8 + bottomSafe),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (onSend != null)
              SizedBox(
                height: 34,
                child: ListView(
                  scrollDirection: Axis.horizontal,
                  children: [
                    _AssistantHint(
                      label: 'Personal code review',
                      prompt:
                          'Review my local repository diff. Focus on correctness, concurrency, and durable state. Give actionable findings with file and line references; skip cosmetic comments.',
                      onSend: onSend,
                    ),
                    _AssistantHint(
                      label: 'My behaviors',
                      prompt:
                          'List my admitted C# behaviors and explain what each one does.',
                      onSend: onSend,
                    ),
                    _AssistantHint(
                      label: 'Create a behavior',
                      prompt:
                          'Help me turn a routine into a C# behavior. Ask what should trigger it and what it should do.',
                      onSend: onSend,
                    ),
                  ],
                ),
              ),
            Row(
              children: [
                IconButton(
                  key: const Key('chat_attach_button'),
                  tooltip: 'Attach',
                  icon: const Icon(Icons.attachment),
                  color: muted,
                  onPressed: onAttach,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: TextField(
                    controller: _text,
                    focusNode: context.read<VoiceComposerController>().focus,
                    minLines: 1,
                    maxLines: 3,
                    textCapitalization: TextCapitalization.sentences,
                    textInputAction: TextInputAction.newline,
                    style: theme.bodyMedium.copyWith(color: theme.onSurface),
                    decoration: InputDecoration(
                      hintText: 'Type a message',
                      hintStyle: theme.bodyMedium.copyWith(color: muted),
                      border: const OutlineInputBorder(
                        borderSide: BorderSide.none,
                        borderRadius: BorderRadius.all(Radius.circular(24)),
                      ),
                      filled: true,
                      fillColor: theme.surfaceHigh.withValues(alpha: 0.8),
                      hoverColor: Colors.transparent,
                    ),
                    onSubmitted: _submit,
                  ),
                ),
                if (widget.canVoice) ...[
                  const SizedBox(width: 8),
                  _VoiceButton(onTap: widget.onVoiceTap),
                ],
                const SizedBox(width: 8),
                ValueListenableBuilder<bool>(
                  valueListenable: _hasText,
                  builder: (context, hasText, _) => IconButton(
                    tooltip: 'Send',
                    icon: const Icon(Icons.send),
                    color: muted,
                    onPressed: hasText && onSend != null
                        ? () => _submit(_text.text)
                        : null,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  void _submit(String text) {
    final trimmed = text.trim();
    if (trimmed.isEmpty) {
      return;
    }
    context.read<OnMessageSendCallback?>()?.call(trimmed);
    _text.clear();
  }

  void _measure() {
    if (!mounted) {
      return;
    }
    final box = _key.currentContext?.findRenderObject() as RenderBox?;
    if (box == null) {
      return;
    }
    context.read<ComposerHeightNotifier>().setHeight(
      box.size.height - MediaQuery.of(context).padding.bottom,
    );
  }
}

final class _AssistantHint extends StatelessWidget {
  const _AssistantHint({
    required this.label,
    required this.prompt,
    required this.onSend,
  });

  final String label;
  final String prompt;
  final OnMessageSendCallback onSend;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(right: 8),
    child: ActionChip(
      key: Key('assistant_hint_${label.toLowerCase().replaceAll(' ', '_')}'),
      avatar: const Icon(Icons.auto_awesome, size: 15),
      label: Text(label),
      onPressed: () => onSend(prompt),
    ),
  );
}

final class _VoiceButton extends StatelessWidget {
  const _VoiceButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final voice = context.watch<VoiceComposerController>();
    final muted = context.select(
      (ChatTheme t) => t.colors.onSurface.withValues(alpha: 0.5),
    );
    return IconButton(
      key: const Key('chat_voice_button'),
      tooltip: voice.recording
          ? 'Stop and send'
          : voice.busy
          ? 'Sending voice'
          : 'Record voice',
      icon: Icon(
        voice.recording
            ? Icons.stop_circle_outlined
            : voice.busy
            ? Icons.hourglass_top
            : Icons.mic,
      ),
      color: voice.recording ? BrainPalette.signal : muted,
      onPressed: voice.busy ? null : onTap,
    );
  }
}
