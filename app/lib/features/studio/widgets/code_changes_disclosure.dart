import 'package:flutter/material.dart';

import '../feature_studio_models.dart';

const Key featureStudioCodePanelKey = Key('feature-studio-code-panel');

class CodeChangesDisclosure extends StatelessWidget {
  const CodeChangesDisclosure({
    super.key,
    required this.source,
    required this.errors,
    required this.onChanged,
    required this.enabled,
  });

  final FeatureStudioSource source;
  final List<String> errors;
  final ValueChanged<FeatureStudioSource> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Card(
    key: featureStudioCodePanelKey,
    margin: EdgeInsets.zero,
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Code & changes',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 4),
          Text(
            '${source.files.length} files · ${source.implementationProjectPath}',
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          for (var index = 0; index < source.files.length; index++)
            _SourceFileEditor(
              key: ValueKey(source.files[index].path),
              file: source.files[index],
              enabled: enabled,
              onChanged: (content) => _replaceFile(index, content),
            ),
          if (errors.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              errors.first,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      ),
    ),
  );

  void _replaceFile(int index, String content) {
    final files = source.files.toList();
    files[index] = FeatureStudioSourceFile(
      path: files[index].path,
      content: content,
    );
    onChanged(
      FeatureStudioSource(
        implementationProjectPath: source.implementationProjectPath,
        scenarioProjectPath: source.scenarioProjectPath,
        files: files,
      ),
    );
  }
}

class _SourceFileEditor extends StatefulWidget {
  const _SourceFileEditor({
    super.key,
    required this.file,
    required this.enabled,
    required this.onChanged,
  });

  final FeatureStudioSourceFile file;
  final bool enabled;
  final ValueChanged<String> onChanged;

  @override
  State<_SourceFileEditor> createState() => _SourceFileEditorState();
}

class _SourceFileEditorState extends State<_SourceFileEditor> {
  late final TextEditingController _content;

  @override
  void initState() {
    super.initState();
    _content = TextEditingController(text: widget.file.content);
  }

  @override
  void didUpdateWidget(covariant _SourceFileEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_content.text == widget.file.content) return;
    _content.value = TextEditingValue(
      text: widget.file.content,
      selection: TextSelection.collapsed(offset: widget.file.content.length),
    );
  }

  @override
  void dispose() {
    _content.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ExpansionTile(
    tilePadding: EdgeInsets.zero,
    title: Text(widget.file.path),
    children: [
      TextFormField(
        key: ValueKey('source-${widget.file.path}'),
        controller: _content,
        enabled: widget.enabled,
        minLines: 4,
        maxLines: 14,
        keyboardType: TextInputType.multiline,
        style: const TextStyle(fontFamily: 'monospace'),
        decoration: const InputDecoration(
          labelText: 'File content',
          alignLabelWithHint: true,
        ),
        onChanged: widget.onChanged,
      ),
      const SizedBox(height: 12),
    ],
  );
}
