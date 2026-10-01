import 'package:flutter/material.dart';

/// Shared bounded collection presentation for files and read-only activity.
class UiCollectionView extends StatelessWidget {
  const UiCollectionView({
    super.key,
    required this.items,
    this.fileMode = true,
    this.selectionEnabled = true,
    this.selectedId,
    this.onSelect,
    this.onActivate,
    this.emptyLabel,
    this.error,
    this.onRetry,
    this.onLoadMore,
    this.loadingMore = false,
  });

  final List<Map<String, dynamic>> items;
  final bool fileMode, selectionEnabled, loadingMore;
  final String? selectedId, emptyLabel, error;
  final ValueChanged<String>? onSelect;
  final ValueChanged<Map<String, dynamic>>? onActivate;
  final VoidCallback? onRetry, onLoadMore;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final content = items.isEmpty
          ? Center(
              child: Text(
                emptyLabel ??
                    (fileMode ? 'This folder is empty.' : 'No activity yet.'),
              ),
            )
          : ListView.separated(
              shrinkWrap: !constraints.hasBoundedHeight,
              physics: constraints.hasBoundedHeight
                  ? null
                  : const NeverScrollableScrollPhysics(),
              itemCount: items.length,
              separatorBuilder: (_, _) => const Divider(height: 1),
              itemBuilder: (context, index) =>
                  _item(context, items[index], constraints.maxWidth),
            );
      return Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (error != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                children: [
                  Semantics(liveRegion: true, child: Text(error!)),
                  if (onRetry != null)
                    TextButton(onPressed: onRetry, child: const Text('Retry')),
                ],
              ),
            ),
          if (constraints.hasBoundedHeight)
            Expanded(child: content)
          else
            content,
          if (onLoadMore != null || loadingMore)
            Padding(
              padding: const EdgeInsets.all(8),
              child: loadingMore
                  ? Semantics(
                      label: 'Loading more',
                      liveRegion: true,
                      child: const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    )
                  : TextButton(
                      onPressed: onLoadMore,
                      child: const Text('Load more'),
                    ),
            ),
        ],
      );
    },
  );

  Widget _item(BuildContext context, Map<String, dynamic> item, double width) {
    final label = item['label'] as String? ?? '';
    final id = item['id'] as String? ?? '';
    final kind = item['kind'];
    final canActivate =
        onActivate != null &&
        (fileMode ? item['canActivate'] == true : item['canActivate'] != false);
    final select = selectionEnabled && onSelect != null
        ? () => onSelect!(id)
        : null;
    final secondary =
        item['secondaryText'] as String? ??
        (fileMode && kind == 'file' ? 'Preview unavailable' : null);
    final trailing =
        item['trailingText'] as String? ??
        (fileMode ? _fileMetadata(item) : '');
    final color = Theme.of(context).colorScheme.primary;
    return Material(
      color: Colors.transparent,
      child: Semantics(
        explicitChildNodes: true,
        button: canActivate,
        label: canActivate
            ? 'Open $label'
            : fileMode && item['canActivate'] != true
            ? '$label, unsupported file'
            : label,
        child: ListTile(
          selected: selectionEnabled && selectedId == id,
          onLongPress: select,
          leading: Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: color.withValues(alpha: .1),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(
              fileMode
                  ? kind == 'folder'
                        ? Icons.folder_outlined
                        : kind == 'image'
                        ? Icons.image_outlined
                        : Icons.insert_drive_file_outlined
                  : switch (kind) {
                      'model' => Icons.auto_awesome_outlined,
                      'tool' => Icons.build_outlined,
                      'data' => Icons.storage_outlined,
                      _ => Icons.bolt_outlined,
                    },
              size: 21,
              color: color,
            ),
          ),
          title: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: secondary == null
              ? null
              : Text(secondary, maxLines: 2, overflow: TextOverflow.ellipsis),
          trailing: trailing.isEmpty && !selectionEnabled
              ? null
              : Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (trailing.isNotEmpty)
                      ConstrainedBox(
                        constraints: BoxConstraints(maxWidth: width * .3),
                        child: Text(
                          trailing,
                          maxLines: 4,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                      ),
                    if (selectionEnabled)
                      IconButton(
                        tooltip: 'Select $label',
                        icon: Icon(
                          selectedId == id
                              ? Icons.check_circle
                              : Icons.radio_button_unchecked,
                          size: 18,
                        ),
                        onPressed: select,
                      ),
                  ],
                ),
          onTap: canActivate ? () => onActivate!(item) : null,
        ),
      ),
    );
  }

  String _fileMetadata(Map<String, dynamic> item) {
    if (item['kind'] == 'folder') return 'Folder';
    final bytes = (item['sizeBytes'] as num?)?.toInt();
    if (bytes == null) return '';
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1048576) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    return '${(bytes / 1048576).toStringAsFixed(1)} MB';
  }
}
