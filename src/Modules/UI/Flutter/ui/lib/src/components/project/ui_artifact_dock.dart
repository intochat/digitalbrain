import 'package:flutter/material.dart';

class UiEditorFrame extends StatelessWidget {
  const UiEditorFrame({super.key, required this.child, this.active = false});
  final Widget child;
  final bool active;
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.all(6),
      child: Material(
        color: colors.surfaceContainerLow,
        elevation: active ? 5 : 1,
        shadowColor: colors.shadow.withValues(alpha: .22),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: BorderSide(
            color: active
                ? colors.primary.withValues(alpha: .6)
                : colors.outlineVariant,
          ),
        ),
        clipBehavior: Clip.antiAlias,
        child: child,
      ),
    );
  }
}

IconData uiArtifactIcon(String kind) => switch (kind) {
  'table' => Icons.table_chart_outlined,
  'chart' => Icons.bar_chart_rounded,
  'diagram' => Icons.draw_outlined,
  'image' => Icons.image_outlined,
  'brain' => Icons.hub_outlined,
  _ => Icons.description_outlined,
};

@immutable
class UiDockItem {
  const UiDockItem({required this.id, required this.title, required this.kind});
  final String id, title, kind;
}

/// A compact shelf of minimized editors. Restore never changes artifact context.
class UiArtifactDock extends StatelessWidget {
  const UiArtifactDock({
    super.key,
    required this.items,
    required this.onRestore,
  });
  final List<UiDockItem> items;
  final ValueChanged<String> onRestore;
  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return const SizedBox.shrink();
    final colors = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
      child: Align(
        alignment: Alignment.bottomCenter,
        child: Material(
          color: colors.surfaceContainerLow,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16),
            side: BorderSide(color: colors.outlineVariant),
          ),
          child: SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Padding(
              padding: const EdgeInsets.all(6),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final item in items)
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 3),
                      child: Tooltip(
                        message: 'Restore ${item.title}',
                        child: InkWell(
                          key: ValueKey('restore-${item.id}'),
                          borderRadius: BorderRadius.circular(10),
                          onTap: () => onRestore(item.id),
                          child: SizedBox(
                            width: 112,
                            child: Padding(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 10,
                              ),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Icon(
                                    uiArtifactIcon(item.kind),
                                    size: 24,
                                    color: colors.primary,
                                  ),
                                  const SizedBox(height: 7),
                                  Text(
                                    item.title,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: Theme.of(context)
                                        .textTheme
                                        .labelSmall,
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
