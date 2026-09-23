import 'package:flutter/material.dart';

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
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('This component could not be loaded.'),
                TextButton(
                  onPressed: () => setState(
                    () => state = widget.load(widget.kind, widget.name),
                  ),
                  child: const Text('Retry'),
                ),
              ],
            ),
          );
        }
        if (!snapshot.hasData) {
          return const Center(child: CircularProgressIndicator());
        }
        final data = snapshot.data!;
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
                obscureText:
                    definition['kind'] == 'secret' ||
                    definition['kind'] == 'password',
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
          default:
            return const Center(
              child: Text('This component cannot be displayed.'),
            );
        }
      },
    );
  }
}
