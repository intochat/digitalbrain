import 'package:flutter/material.dart';

/// What a person chose at a first-use grant prompt.
enum GrantDecision { once, always, deny }

/// The first-use prompt shown when an app first needs one of the owner's fields:
/// `Allow <app> to read <field>? Once / Always`. The choice is stored as a grant.
class GrantPrompt extends StatelessWidget {
  const GrantPrompt({
    super.key,
    required this.appName,
    required this.fieldLabel,
    required this.onDecision,
  });

  final String appName;
  final String fieldLabel;
  final ValueChanged<GrantDecision> onDecision;

  @override
  Widget build(BuildContext context) => Semantics(
    container: true,
    explicitChildNodes: true,
    label: 'Allow $appName to read $fieldLabel?',
    child: Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Allow $appName to read $fieldLabel?',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              OutlinedButton(
                onPressed: () => onDecision(GrantDecision.once),
                child: const Text('Once'),
              ),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: () => onDecision(GrantDecision.always),
                child: const Text('Always'),
              ),
              const Spacer(),
              TextButton(
                onPressed: () => onDecision(GrantDecision.deny),
                child: const Text('Not now'),
              ),
            ],
          ),
        ],
      ),
    ),
  );
}

/// Shows the first-use prompt and resolves the person's choice.
Future<GrantDecision> showGrantPrompt(
  BuildContext context, {
  required String appName,
  required String fieldLabel,
}) async {
  final decision = await showModalBottomSheet<GrantDecision>(
    context: context,
    showDragHandle: true,
    builder: (promptContext) => GrantPrompt(
      appName: appName,
      fieldLabel: fieldLabel,
      onDecision: (choice) => Navigator.of(promptContext).pop(choice),
    ),
  );
  return decision ?? GrantDecision.deny;
}