import 'package:flutter/material.dart';

const csharpStarterSource = 'Console.WriteLine("Hello from a C# app.");\n';

final csharpFileIdPattern = RegExp(r'^[A-Za-z0-9_-]{1,100}$');

typedef CSharpNewFile = ({String id, String name, String purpose});

class CSharpNewFileDialog extends StatefulWidget {
  const CSharpNewFileDialog({super.key});
  @override
  State<CSharpNewFileDialog> createState() => _CSharpNewFileDialogState();
}

class _CSharpNewFileDialogState extends State<CSharpNewFileDialog> {
  final form = GlobalKey<FormState>();
  final id = TextEditingController();
  final name = TextEditingController();
  final purpose = TextEditingController();

  @override
  void dispose() {
    id.dispose();
    name.dispose();
    purpose.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('New C# file'),
    content: SizedBox(
      width: 460,
      child: SingleChildScrollView(
        child: Form(
          key: form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                key: const ValueKey('csharp-new-id'),
                controller: id,
                autofocus: true,
                decoration: const InputDecoration(
                  labelText: 'ID',
                  hintText: 'timer-status',
                  helperText: 'Letters, digits, - and _ only.',
                ),
                validator: (value) =>
                    csharpFileIdPattern.hasMatch(value?.trim() ?? '')
                    ? null
                    : 'Use 1-100 letters, digits, - or _.',
              ),
              TextFormField(
                key: const ValueKey('csharp-new-name'),
                controller: name,
                maxLength: 120,
                decoration: const InputDecoration(labelText: 'Name'),
              ),
              TextFormField(
                key: const ValueKey('csharp-new-purpose'),
                controller: purpose,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(
                  labelText: 'What should this app do?',
                ),
              ),
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () {
          if (!form.currentState!.validate()) return;
          final trimmedId = id.text.trim();
          final trimmedName = name.text.trim();
          Navigator.pop<CSharpNewFile>(context, (
            id: trimmedId,
            name: trimmedName.isEmpty ? trimmedId : trimmedName,
            purpose: purpose.text.trim(),
          ));
        },
        child: const Text('Create'),
      ),
    ],
  );
}

class CSharpShareDialog extends StatefulWidget {
  const CSharpShareDialog({super.key, required this.suggestedName});
  final String suggestedName;
  @override
  State<CSharpShareDialog> createState() => _CSharpShareDialogState();
}

class _CSharpShareDialogState extends State<CSharpShareDialog> {
  late final packageName = TextEditingController(text: widget.suggestedName);

  @override
  void dispose() {
    packageName.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Share as package'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Publishes the saved source and its settings. Anyone can install it from Apps.',
        ),
        TextField(
          key: const ValueKey('share-package-name'),
          controller: packageName,
          decoration: const InputDecoration(labelText: 'Package name'),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(context, packageName.text.trim()),
        child: const Text('Share'),
      ),
    ],
  );
}

Future<bool> confirmCSharpDelete(BuildContext context, String name) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Delete "$name"?'),
        content: const Text('This stops the app and removes its source.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    ) ==
    true;
