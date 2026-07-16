import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

const Key inoComposerKey = Key('v2-ino-composer');
const Key inoComposerFieldKey = Key('v2-ino-composer-field');
const Key inoSendButtonKey = Key('v2-ino-send-button');
const int inoMaximumPromptLength = 4096;

class InoComposer extends StatelessWidget {
  const InoComposer({
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
      key: inoComposerKey,
      color: Theme.of(context).colorScheme.surfaceContainerLow,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Semantics(
                label: 'Message DigitalBrain',
                textField: true,
                child: TextField(
                  key: inoComposerFieldKey,
                  controller: controller,
                  focusNode: focusNode,
                  minLines: 1,
                  maxLines: 5,
                  maxLength: inoMaximumPromptLength,
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
              label: 'Send message to DigitalBrain',
              child: FilledButton(
                key: inoSendButtonKey,
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
