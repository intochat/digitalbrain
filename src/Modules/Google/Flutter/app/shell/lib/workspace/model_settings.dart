import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

typedef ModelCatalogLoader = Future<ChatModelCatalog> Function();

/// The same configured catalog is used for a default and a conversation override.
class ModelSettings extends StatefulWidget {
  const ModelSettings({
    super.key,
    this.load,
    this.selectedId,
    required this.onSelected,
    this.conversation = false,
  });
  final ModelCatalogLoader? load;
  final String? selectedId;
  final ValueChanged<String?> onSelected;
  final bool conversation;
  @override
  State<ModelSettings> createState() => _ModelSettingsState();
}

class _ModelSettingsState extends State<ModelSettings> {
  ChatModelCatalog? _catalog;
  String? _error;
  bool _loading = false;
  int _generation = 0;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(ModelSettings oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.load != widget.load) {
      _catalog = null;
      _error = null;
      _loading = false;
      _load();
    }
  }

  Future<void> _load() async {
    final generation = ++_generation;
    if (widget.load == null) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final catalog = await widget.load!();
      if (mounted && generation == _generation) {
        setState(() => _catalog = catalog);
      }
    } catch (_) {
      if (mounted && generation == _generation) {
        setState(() => _error = 'Models could not be loaded.');
      }
    } finally {
      if (mounted && generation == _generation) {
        setState(() => _loading = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final options = _catalog == null
        ? <ChatModelOption>[]
        : [_catalog!.automatic, ..._catalog!.models];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          widget.conversation ? 'Model for this conversation' : 'Default model',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 8),
        Text(
          widget.conversation
              ? 'Applies to your next message. Previous messages stay in the conversation.'
              : 'Used for new conversations on this device. You can choose a different model beside the chat composer.',
        ),
        const SizedBox(height: 20),
        if (widget.load == null)
          const Text('Connect to your workspace to see configured models.'),
        if (_loading) const LinearProgressIndicator(),
        if (_error != null) ...[
          Text(_error!),
          TextButton(
            onPressed: _loading ? null : _load,
            child: const Text('Retry'),
          ),
        ],
        if (_catalog != null &&
            widget.selectedId != null &&
            !options.any((m) => m.id == widget.selectedId))
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: Text(
              'Your saved model is no longer available. Choose another model or Automatic.',
            ),
          ),
        for (final option in options)
          Card(
            margin: const EdgeInsets.only(bottom: 8),
            child: ListTile(
              selected: widget.selectedId == option.id,
              leading: Icon(
                widget.selectedId == option.id
                    ? Icons.radio_button_checked
                    : Icons.radio_button_off,
              ),
              title: Text(option.label),
              subtitle: Text(
                [
                  if (option.provider != null) option.provider!,
                  if (option.model != null) option.model!,
                  if (!option.available) 'Not configured',
                  if (option.capabilities.isNotEmpty)
                    option.capabilities.join(' · '),
                ].join(' · '),
              ),
              onTap: (option.available || option.id == null) && !_loading
                  ? () => widget.onSelected(option.id)
                  : null,
            ),
          ),
        if (_catalog != null && _catalog!.models.isEmpty)
          const Text(
            'No selectable models are configured. Providers are managed by your workspace administrator.',
          ),
        if (!widget.conversation) ...[
          const SizedBox(height: 16),
          const Text(
            'Only configured chat models are listed. Provider credentials stay on the server.',
          ),
        ],
      ],
    );
  }
}

class ConversationModelPicker extends StatelessWidget {
  const ConversationModelPicker({
    super.key,
    required this.load,
    this.selectedId,
    required this.onSelected,
    this.enabled = true,
  });
  final ModelCatalogLoader load;
  final String? selectedId;
  final ValueChanged<String?> onSelected;
  final bool enabled;
  @override
  Widget build(BuildContext context) => Tooltip(
    message: 'Choose model',
    child: TextButton.icon(
      key: const Key('conversation-model-picker'),
      icon: const Icon(Icons.tune, size: 14),
      label: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 115),
        child: Text(
          selectedId == null ? 'Auto' : selectedId!.split(':').last,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 12),
        ),
      ),
      onPressed: !enabled
          ? null
          : () => showDialog<void>(
              context: context,
              builder: (dialogContext) => Dialog(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(
                    maxWidth: 520,
                    maxHeight: 640,
                  ),
                  child: SingleChildScrollView(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Align(
                          alignment: Alignment.centerRight,
                          child: IconButton(
                            tooltip: 'Close model picker',
                            onPressed: () => Navigator.pop(dialogContext),
                            icon: const Icon(Icons.close),
                          ),
                        ),
                        ModelSettings(
                          load: load,
                          selectedId: selectedId,
                          conversation: true,
                          onSelected: (id) {
                            onSelected(id);
                            Navigator.pop(dialogContext);
                          },
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
    ),
  );
}
