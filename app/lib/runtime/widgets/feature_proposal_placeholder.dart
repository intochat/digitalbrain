import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

const Key featureProposalIdKey = Key('feature-proposal-id');
const Key featureProposalBackToChatButtonKey = Key(
  'feature-proposal-back-to-chat-button',
);

class FeatureProposalPlaceholder extends StatelessWidget {
  const FeatureProposalPlaceholder({super.key, required this.proposalId});

  final String proposalId;

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Feature Studio',
                  style: Theme.of(context).textTheme.headlineMedium,
                ),
                const SizedBox(height: 8),
                Text(
                  'Draft created from Chat',
                  style: Theme.of(context).textTheme.bodyLarge,
                ),
                const SizedBox(height: 16),
                SelectableText(proposalId, key: featureProposalIdKey),
                const SizedBox(height: 24),
                OutlinedButton(
                  key: featureProposalBackToChatButtonKey,
                  onPressed: () => context.go('/chat'),
                  child: const Text('Back to Chat'),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}
