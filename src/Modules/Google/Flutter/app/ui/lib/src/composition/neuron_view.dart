import 'package:flutter/material.dart';

import '../models/ui_part.dart';
import 'renderer_registry.dart';

typedef NeuronLoader = Future<Map<String, dynamic>> Function(
  String kind,
  String name,
);

class NeuronView extends StatefulWidget {
  const NeuronView({
    super.key,
    required this.kind,
    required this.name,
    required this.load,
    this.onActivate,
    this.onAction,
    this.imageBuilder,
    this.ancestors = const {},
    this.revision = 0,
    this.enabled = true,
  });
  final String kind, name;
  final NeuronLoader load;
  final ValueChanged<Map<String, dynamic>>? onActivate;
  final Future<void> Function(Map<String, dynamic>)? onAction;
  final Widget Function(Map<String, dynamic>)? imageBuilder;
  final Set<String> ancestors;
  final int revision;
  final bool enabled;
  @override
  State<NeuronView> createState() => _NeuronViewState();
}

class _NeuronViewState extends State<NeuronView> {
  late Future<Map<String, dynamic>> state;
  String? selection;
  @override
  void initState() {
    super.initState();
    state = widget.load(widget.kind, widget.name);
  }

  @override
  void didUpdateWidget(NeuronView old) {
    super.didUpdateWidget(old);
    if (old.name != widget.name ||
        old.kind != widget.kind ||
        old.revision != widget.revision) {
      state = widget.load(widget.kind, widget.name);
    }
  }

  Future<void> selectItem(
    Map<String, dynamic> item,
    Map<String, dynamic> data,
  ) async {
    setState(() => selection = item['id'] as String);
    await widget.onAction?.call({
      'kind': 'collection',
      'name': widget.name,
      'action': 'select',
      'value': item['id'],
      'revision': data['revision'],
    });
    if (mounted) setState(() => state = widget.load(widget.kind, widget.name));
  }

  @override
  Widget build(BuildContext context) {
    final identity = '${widget.kind}:${widget.name}';
    if (widget.ancestors.contains(identity) || widget.ancestors.length >= 16) {
      return const Center(child: Text('This component cannot be displayed.'));
    }
    return FutureBuilder<Map<String, dynamic>>(
      future: state,
      builder: (context, snapshot) {
        if (snapshot.hasError) {
          return WindowStateView(
            state: const WindowState(WindowStatus.failed),
            onRetry: () => setState(
              () => state = widget.load(widget.kind, widget.name),
            ),
          );
        }
        if (!snapshot.hasData) {
          return const WindowStateView(state: WindowState(WindowStatus.loading));
        }
        final data = snapshot.data!;
        final declared = WindowState.fromMetadata(data);
        if (declared != null) {
          return WindowStateView(
            state: declared,
            onRetry: () => setState(
              () => state = widget.load(widget.kind, widget.name),
            ),
          );
        }
        final definition = Map<String, dynamic>.from(
          data['definition'] as Map? ?? data,
        );
        final children = (definition['children'] as List? ?? [])
            .take(128)
            .map(
              (child) =>
                  child is! Map ||
                      child['kind'] is! String ||
                      child['name'] is! String
                  ? const Center(
                      child: Text('This component cannot be displayed.'),
                    )
                  : NeuronView(
                      key: ValueKey('${child['kind']}:${child['name']}'),
                      kind: child['kind'] as String,
                      name: child['name'] as String,
                      load: widget.load,
                      onActivate: widget.onActivate,
                      onAction: widget.onAction,
                      imageBuilder: widget.imageBuilder,
                      ancestors: {...widget.ancestors, identity},
                      revision: widget.revision,
                      enabled: widget.enabled,
                    ),
            )
            .toList();
        switch (widget.kind) {
          case 'surface':
          case 'layout':
            if (children.isEmpty) return const SizedBox.shrink();
            final gap = (definition['gap'] as num? ?? 0).toDouble().clamp(
              0.0,
              128.0,
            );
            if (definition['mode'] == 'stack') {
              return Stack(fit: StackFit.expand, children: children);
            }
            return Flex(
              direction:
                  definition['mode'] == 'row' || definition['mode'] == 'split'
                  ? Axis.horizontal
                  : Axis.vertical,
              spacing: gap,
              children: [
                for (var i = 0; i < children.length; i++)
                  if ((definition['extents'] as List?)?.elementAtOrNull(i)
                      case final num extent when extent > 0)
                    SizedBox(
                      width:
                          definition['mode'] == 'row' ||
                              definition['mode'] == 'split'
                          ? extent.toDouble()
                          : null,
                      height:
                          definition['mode'] == 'row' ||
                              definition['mode'] == 'split'
                          ? null
                          : extent.toDouble(),
                      child: children[i],
                    )
                  else
                    Expanded(child: children[i]),
              ],
            );
          case 'text':
            return Text(definition['markdown'] as String? ?? '');
          case 'button':
            return TextButton(
              onPressed: definition['enabled'] == false
                  ? null
                  : () => widget.onAction?.call({
                      'kind': 'button',
                      'name': widget.name,
                      'action': definition['action'],
                    }),
              child: Text(
                definition['label'] as String? ?? '',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            );
          case 'textfield':
            return Padding(
              padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
              child: TextFormField(
                key: ValueKey('${widget.name}:${widget.revision}'),
                initialValue: definition['value'] as String? ?? '',
                obscureText: definition['kind'] == 'secret',
                decoration: InputDecoration(
                  labelText: definition['label'] as String? ?? '',
                  isDense: true,
                ),
                onFieldSubmitted: (value) => widget.onAction?.call({
                  'kind': 'textfield',
                  'name': widget.name,
                  'value': value,
                }),
              ),
            );
          case 'tabs':
            final tabs = (definition['tabs'] as List? ?? [])
                .whereType<Map>()
                .toList();
            final selected = definition['selectedId'];
            final active = tabs
                .where((tab) => tab['id'] == selected)
                .firstOrNull;
            return Column(
              children: [
                SizedBox(
                  height: 42,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      for (final tab in tabs)
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 4),
                          child: ChoiceChip(
                            label: Text(tab['title'] as String? ?? ''),
                            selected: tab['id'] == selected,
                            onSelected: !widget.enabled
                                ? null
                                : (_) => widget.onAction?.call({
                                    'kind': 'tabs',
                                    'name': widget.name,
                                    'value': tab['id'],
                                  }),
                          ),
                        ),
                    ],
                  ),
                ),
                if (active?['child'] is Map)
                  Expanded(
                    child: NeuronView(
                      kind: active!['child']['kind'] as String,
                      name: active['child']['name'] as String,
                      load: widget.load,
                      onActivate: widget.onActivate,
                      onAction: widget.onAction,
                      imageBuilder: widget.imageBuilder,
                      ancestors: {...widget.ancestors, identity},
                      revision: widget.revision,
                      enabled: widget.enabled,
                    ),
                  ),
              ],
            );
          case 'card':
            return SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(definition['title'] as String? ?? ''),
                  Text(definition['body'] as String? ?? ''),
                  ...children.map(
                    (child) => SizedBox(height: 240, child: child),
                  ),
                ],
              ),
            );
          case 'collection':
            final items = (definition['items'] as List? ?? []).cast<Map>();
            if (items.isEmpty) {
              return const Center(child: Text('This folder is empty.'));
            }
            return ListView.separated(
              itemCount: items.length,
              separatorBuilder: (_, _) => const Divider(height: 1),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index]);
                final enabled = item['canActivate'] == true;
                final kind = item['kind'];
                final bytes = (item['sizeBytes'] as num?)?.toInt();
                final meta = kind == 'folder'
                    ? 'Folder'
                    : bytes == null
                    ? ''
                    : bytes < 1024
                    ? '$bytes B'
                    : bytes < 1048576
                    ? '${(bytes / 1024).toStringAsFixed(1)} KB'
                    : '${(bytes / 1048576).toStringAsFixed(1)} MB';
                return Material(
                  color: Colors.transparent,
                  child: Semantics(
                    explicitChildNodes: true,
                    button: enabled,
                    label: enabled
                        ? 'Open ${item['label']}'
                        : '${item['label']}, unsupported file',
                    child: ListTile(
                      selected:
                          (selection ?? definition['selection']) == item['id'],
                      onLongPress: () => selectItem(item, data),
                      leading: Container(
                        width: 38,
                        height: 38,
                        decoration: BoxDecoration(
                          color: Theme.of(context).colorScheme.primary
                              .withValues(alpha: .1),
                          borderRadius: BorderRadius.circular(10),
                        ),
                        child: Icon(
                          kind == 'folder'
                              ? Icons.folder_outlined
                              : kind == 'image'
                              ? Icons.image_outlined
                              : Icons.insert_drive_file_outlined,
                          size: 21,
                          color: Theme.of(context).colorScheme.primary,
                        ),
                      ),
                      title: Text(
                        item['label'] as String,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      trailing: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            meta,
                            style: Theme.of(context).textTheme.labelSmall,
                          ),
                          IconButton(
                            tooltip: 'Select ${item['label']}',
                            icon: Icon(
                              (selection ?? definition['selection']) ==
                                      item['id']
                                  ? Icons.check_circle
                                  : Icons.radio_button_unchecked,
                              size: 18,
                            ),
                            onPressed: () => selectItem(item, data),
                          ),
                        ],
                      ),
                      subtitle: kind == 'file'
                          ? const Text('Preview unavailable')
                          : null,
                      onTap: enabled
                          ? () => widget.onActivate?.call({
                              ...item,
                              'collection': widget.name,
                              'revision': data['revision'],
                            })
                          : null,
                    ),
                  ),
                );
              },
            );
          case 'imagecanvas':
            return widget.imageBuilder?.call(definition) ??
                const Center(child: Text('Image canvas unavailable.'));
          case 'form':
            return _FormRenderer(
              part: UiFormPart.fromMetadata(definition),
              name: widget.name,
              revision: widget.revision,
              enabled: widget.enabled,
              onAction: widget.onAction,
            );
          default:
            return _FallbackRenderer(kind: widget.kind);
        }
      },
    );
  }
}

/// Renders the declared fallback for a UI kind that has no dedicated renderer: the kind is named
/// and explained, never blank.
class _FallbackRenderer extends StatelessWidget {
  const _FallbackRenderer({required this.kind});

  final String kind;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('${RendererRegistry.fallbackLabel}: $kind'),
            const SizedBox(height: 4),
            const Text(
              RendererRegistry.fallbackExplanation,
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}

/// Renders each declared window state. Loading shows progress, failed offers a retry, and the
/// other states explain the situation in one line so no window is blank.
class WindowStateView extends StatelessWidget {
  const WindowStateView({super.key, required this.state, this.onRetry});

  final WindowState state;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    if (state.status == WindowStatus.loading) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: CircularProgressIndicator(),
        ),
      );
    }
    final (icon, label, message) = switch (state.status) {
      WindowStatus.loading => (Icons.hourglass_empty, 'Loading', ''),
      WindowStatus.empty => (
        Icons.inbox_outlined,
        'Nothing here yet',
        'This window has no content yet.',
      ),
      WindowStatus.permissionDenied => (
        Icons.lock_outline,
        'Not available to you',
        'You do not have permission to view this window.',
      ),
      WindowStatus.expiredConnection => (
        Icons.link_off,
        'Connection expired',
        'The connection this window needs has expired. Reconnect to continue.',
      ),
      WindowStatus.failed => (
        Icons.error_outline,
        'Could not load',
        state.message ?? 'This component could not be loaded.',
      ),
    };
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 28),
            const SizedBox(height: 8),
            Text(label),
            if (message.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(message, textAlign: TextAlign.center),
            ],
            if (onRetry != null) ...[
              const SizedBox(height: 8),
              TextButton(onPressed: onRetry, child: const Text('Retry')),
            ],
          ],
        ),
      ),
    );
  }
}

/// Renders the first-run workspace state: the assistant plus starter prompts that match the
/// connected sources.
class FirstRunView extends StatelessWidget {
  const FirstRunView({super.key, required this.state, this.onPrompt});

  final FirstRunState state;
  final ValueChanged<StarterPrompt>? onPrompt;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            state.assistantTitle,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          const Text('Start with one of these.'),
          const SizedBox(height: 12),
          for (final prompt in state.prompts)
            Card(
              child: ListTile(
                title: Text(prompt.label),
                subtitle: prompt.source == 'assistant'
                    ? null
                    : Text(prompt.source),
                onTap: onPrompt == null ? null : () => onPrompt!(prompt),
              ),
            ),
        ],
      ),
    );
  }
}

/// Renders a declarative form from one read: typed fields, a date picker and a masked secret
/// input. Field edits dispatch a `form` event; Save submits the whole form atomically.
class _FormRenderer extends StatefulWidget {
  const _FormRenderer({
    required this.part,
    required this.name,
    required this.revision,
    required this.enabled,
    required this.onAction,
  });

  final UiFormPart part;
  final String name;
  final int revision;
  final bool enabled;
  final Future<void> Function(Map<String, dynamic>)? onAction;

  @override
  State<_FormRenderer> createState() => _FormRendererState();
}

class _FormRendererState extends State<_FormRenderer> {
  late Map<String, String?> values = _initial();

  Map<String, String?> _initial() => {
    for (final field in widget.part.fields) field.name: field.value,
  };

  @override
  void didUpdateWidget(_FormRenderer old) {
    super.didUpdateWidget(old);
    if (old.revision != widget.revision) {
      values = _initial();
    }
  }

  Future<void> dispatch(Map<String, Object?> event) async {
    await widget.onAction?.call({
      'kind': 'form',
      'name': widget.name,
      'revision': widget.revision,
      ...event,
    });
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (widget.part.title.isNotEmpty) ...[
            Text(widget.part.title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
          ],
          for (final field in widget.part.fields) _field(context, field),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton(
                onPressed: widget.enabled
                    ? () => dispatch({'action': 'submit'})
                    : null,
                child: const Text('Save'),
              ),
              if (widget.part.submitted) ...[
                const SizedBox(width: 8),
                const Text('Saved'),
              ],
            ],
          ),
        ],
      ),
    );
  }

  Widget _field(BuildContext context, UiFormField field) {
    final label = field.required ? '${field.label} *' : field.label;
    switch (field.kind) {
      case 'Secret':
        // The raw secret never reaches the form; it is set out of band and shown as a handle.
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: TextFormField(
            key: Key('form_secret_${field.name}'),
            obscureText: true,
            readOnly: true,
            enabled: widget.enabled,
            decoration: InputDecoration(
              labelText: label,
              isDense: true,
              helperText: field.secretSet ? '•••• set' : 'Not set',
            ),
          ),
        );
      case 'Date':
        final value = values[field.name];
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Row(
            children: [
              Expanded(
                child: InputDecorator(
                  decoration: InputDecoration(labelText: label, isDense: true),
                  child: Text(
                    value == null || value.isEmpty ? 'Pick a date' : value,
                  ),
                ),
              ),
              IconButton(
                key: Key('form_date_${field.name}'),
                icon: const Icon(Icons.calendar_today),
                tooltip: 'Pick ${field.label}',
                onPressed: widget.enabled ? () => _pickDate(field) : null,
              ),
            ],
          ),
        );
      case 'Boolean':
        return SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: values[field.name] == 'true',
          title: Text(label),
          onChanged: widget.enabled
              ? (on) => dispatch({
                  'field': field.name,
                  'value': on ? 'true' : 'false',
                })
              : null,
        );
      case 'Choice':
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: DropdownButtonFormField<String>(
            initialValue: field.choices.contains(values[field.name])
                ? values[field.name]
                : null,
            decoration: InputDecoration(labelText: label, isDense: true),
            items: [
              for (final choice in field.choices)
                DropdownMenuItem(value: choice, child: Text(choice)),
            ],
            onChanged: widget.enabled
                ? (value) => dispatch({'field': field.name, 'value': value})
                : null,
          ),
        );
      default:
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: TextFormField(
            key: Key('form_field_${field.name}'),
            initialValue: values[field.name] ?? '',
            enabled: widget.enabled,
            decoration: InputDecoration(
              labelText: label,
              isDense: true,
              helperText: field.supported ? null : 'Shown as plain text.',
            ),
            onFieldSubmitted: (value) =>
                dispatch({'field': field.name, 'value': value}),
          ),
        );
    }
  }

  Future<void> _pickDate(UiFormField field) async {
    final current = values[field.name];
    final parsed = current == null || current.isEmpty
        ? null
        : DateTime.tryParse(current);
    final picked = await showDatePicker(
      context: context,
      initialDate: parsed ?? DateTime.now(),
      firstDate: DateTime(1900),
      lastDate: DateTime(2200),
    );
    if (picked == null) {
      return;
    }
    final formatted = picked.toIso8601String().split('T').first;
    setState(() => values[field.name] = formatted);
    await dispatch({'field': field.name, 'value': formatted});
  }
}
