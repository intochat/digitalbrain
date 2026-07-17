import 'package:flutter/material.dart';

import '../theme/brain_theme.dart';

class ConversationView extends StatelessWidget {
  const ConversationView({required this.data, super.key});

  final Map<String, dynamic> data;

  @override
  Widget build(BuildContext context) {
    final rawMessages = data['messages'];
    final messages = rawMessages is List ? rawMessages : const <dynamic>[];

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: messages.map((entry) {
        final message = entry is Map<String, dynamic>
            ? entry
            : <String, dynamic>{};
        final text = message['text']?.toString() ?? '';
        final at = message['at']?.toString() ?? '';
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(text, style: const TextStyle(color: BrainColors.ink)),
              Text(
                at,
                style: BrainTheme.mono(
                  const TextStyle(fontSize: 11, color: BrainColors.inkFaint),
                ),
              ),
            ],
          ),
        );
      }).toList(),
    );
  }
}
