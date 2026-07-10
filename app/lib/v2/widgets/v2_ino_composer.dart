import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

const Key v2InoComposerKey = Key('v2-ino-composer');
const Key v2InoComposerFieldKey = Key('v2-ino-composer-field');
const Key v2InoSendButtonKey = Key('v2-ino-send-button');
const int v2InoMaximumPromptLength = 4096;

class V2InoComposer extends StatelessWidget {
  const V2InoComposer({
    super.key,
    required this.controller,
    required this.focusNode,
    required this.canSend,
    required this.onSend,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final bool canSend;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context) {
    return Material(
      key: v2InoComposerKey,
      color: Theme.of(context).colorScheme.surfaceContainerLow,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Semantics(
                label: 'Message INO',
                textField: true,
                child: TextField(
                  key: v2InoComposerFieldKey,
                  controller: controller,
                  focusNode: focusNode,
                  minLines: 1,
                  maxLines: 5,
                  maxLength: v2InoMaximumPromptLength,
                  maxLengthEnforcement: MaxLengthEnforcement.enforced,
                  textCapitalization: TextCapitalization.sentences,
                  textInputAction: TextInputAction.send,
                  onSubmitted: (_) {
                    if (canSend) onSend();
                  },
                  decoration: const InputDecoration(
                    hintText: 'Ask about this workspace',
                    border: InputBorder.none,
                  ),
                ),
              ),
            ),
            const SizedBox(width: 8),
            Semantics(
              button: true,
              label: 'Send message to INO',
              child: FilledButton(
                key: v2InoSendButtonKey,
                onPressed: canSend ? onSend : null,
                child: const Text('Send'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
