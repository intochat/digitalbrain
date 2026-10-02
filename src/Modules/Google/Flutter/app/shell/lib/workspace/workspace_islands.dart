import 'dart:ui';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'workspace_store.dart';

class WorkspaceIslands extends StatelessWidget {
  const WorkspaceIslands({
    super.key,
    required this.store,
    this.apps = const [],
    required this.onLaunch,
    required this.onRestore,
    required this.onNewWorkspace,
    required this.onSavedWork,
    required this.onSearch,
    required this.onSettings,
    this.onCompute,
    this.onSwitchAccount,
    this.onApps,
  });
  final WorkspaceStore store;
  final List<InstalledAppSummary> apps;
  final ValueChanged<String> onLaunch, onRestore;
  final VoidCallback onNewWorkspace, onSavedWork, onSearch, onSettings;
  final VoidCallback? onCompute;
  final VoidCallback? onSwitchAccount, onApps;
  Widget island(BuildContext context, Widget child) => ClipRRect(
    borderRadius: BorderRadius.circular(19),
    child: BackdropFilter(
      filter: ImageFilter.blur(sigmaX: 20, sigmaY: 20),
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.surface.withValues(alpha: .9),
          border: Border.all(
            color: Theme.of(context).colorScheme.outlineVariant
                .withValues(alpha: .45),
          ),
          borderRadius: BorderRadius.circular(19),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: .14),
              blurRadius: 24,
              offset: const Offset(0, 6),
            ),
          ],
        ),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
          child: child,
        ),
      ),
    ),
  );
  IconData appIcon(String kind) => switch (kind) {
    'files' => Icons.folder_outlined,
    'images' => Icons.tune,
    'csharp' => Icons.code,
    'assistant' => Icons.assistant_outlined,
    'table' => Icons.table_chart_outlined,
    _ => Icons.web_asset_outlined,
  };
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final narrow = c.maxWidth < 900;
      final project = store.currentProject;
      final workspace = island(
        context,
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            PopupMenuButton<String>(
              tooltip: 'Workspaces',
              position: PopupMenuPosition.over,
              onSelected: (id) {
                if (id == 'new') {
                  onNewWorkspace();
                } else if (id == 'saved') {
                  onSavedWork();
                } else {
                  store.selectProject(id);
                }
              },
              itemBuilder: (_) => [
                for (final p in store.projects)
                  CheckedPopupMenuItem(
                    value: p.id,
                    checked: p.id == project.id,
                    child: Text(p.title),
                  ),
                const PopupMenuDivider(),
                const PopupMenuItem(value: 'new', child: Text('New workspace')),
                const PopupMenuItem(value: 'saved', child: Text('Saved work')),
              ],
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 8,
                  vertical: 10,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.blur_on,
                      size: 23,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                    if (!narrow) ...[
                      const SizedBox(width: 9),
                      const Text(
                        'IntoChat',
                        style: TextStyle(
                          fontWeight: FontWeight.w600,
                          letterSpacing: -.5,
                        ),
                      ),
                      const Padding(
                        padding: EdgeInsets.symmetric(horizontal: 10),
                        child: Text('/', style: TextStyle(color: Colors.grey)),
                      ),
                      ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 140),
                        child: Text(
                          project.title,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.labelMedium,
                        ),
                      ),
                    ],
                    const SizedBox(width: 6),
                    const Icon(Icons.unfold_more, size: 16),
                  ],
                ),
              ),
            ),
          ],
        ),
      );
      final assistantIsland = island(
        context,
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            IconButton(
              tooltip: 'Assistant',
              onPressed: store.toggleChat,
              icon: Icon(
                Icons.chat_bubble_outline,
                size: 21,
                color: project.presentation.chatCollapsed
                    ? null
                    : Theme.of(context).colorScheme.primary,
              ),
            ),
            if (project.presentation.openArtifactIds.isNotEmpty)
              Container(
                height: 24,
                width: 1,
                margin: const EdgeInsets.symmetric(horizontal: 5),
                color: Theme.of(context).dividerColor,
              ),
            for (final id in project.presentation.openArtifactIds.take(
              narrow ? 3 : 7,
            ))
              Builder(
                builder: (context) {
                  final a = project.artifacts
                      .where((a) => a.id == id)
                      .firstOrNull;
                  if (a == null) return const SizedBox.shrink();
                  final active =
                      project.presentation.activeArtifactId == id &&
                      !project.presentation.minimizedArtifactIds.contains(id);
                  return Tooltip(
                    message: a.title,
                    child: Stack(
                      alignment: Alignment.bottomCenter,
                      children: [
                        IconButton(
                          tooltip: a.title,
                          onPressed: () => onRestore(id),
                          icon: Icon(
                            appIcon(a.data['app'] as String? ?? a.kind),
                            size: 22,
                            color: active
                                ? Theme.of(context).colorScheme.primary
                                : null,
                          ),
                        ),
                        Positioned(
                          bottom: 1,
                          child: Container(
                            width: active ? 12 : 4,
                            height: 3,
                            decoration: BoxDecoration(
                              color: active
                                  ? Theme.of(context).colorScheme.primary
                                  : Colors.grey,
                              borderRadius: BorderRadius.circular(3),
                            ),
                          ),
                        ),
                      ],
                    ),
                  );
                },
              ),
            PopupMenuButton<String>(
              tooltip: 'Applications',
              position: PopupMenuPosition.over,
              icon: const Icon(Icons.apps_rounded, size: 23),
              onSelected: (value) {
                if (value == 'manage-apps') {
                  onApps?.call();
                } else {
                  onLaunch(value);
                }
              },
              itemBuilder: (_) => [
                for (final entry in [
                  (
                    id: 'files',
                    title: 'Files',
                    description: 'Workspace assets',
                    icon: Icons.folder_outlined,
                  ),
                  (
                    id: 'images',
                    title: 'Image Editor',
                    description: 'Draw, crop and export',
                    icon: Icons.tune,
                  ),
                  (
                    id: 'assistant',
                    title: 'Assistant',
                    description: 'Chat or talk with the assistant',
                    icon: Icons.assistant_outlined,
                  ),
                  if (store.developerMode)
                    (
                      id: 'csharp',
                      title: 'C# files',
                      description: 'Write and run single-file C# apps',
                      icon: Icons.code,
                    ),
                  for (final app in apps.where(
                    (app) => app.operations.contains('open'),
                  ))
                    (
                      id: app.id,
                      title: app.title,
                      description: app.description,
                      icon: Icons.web_asset_outlined,
                    ),
                ])
                  PopupMenuItem(
                    value: entry.id,
                    child: ListTile(
                      leading: Icon(entry.icon),
                      title: Text(entry.title),
                      subtitle: Text(entry.description),
                      contentPadding: EdgeInsets.zero,
                    ),
                  ),
                if (onApps != null) ...[
                  const PopupMenuDivider(),
                  const PopupMenuItem(
                    value: 'manage-apps',
                    child: ListTile(
                      leading: Icon(Icons.extension_outlined),
                      title: Text('Apps'),
                      subtitle: Text('Browse and manage apps'),
                      contentPadding: EdgeInsets.zero,
                    ),
                  ),
                ],
              ],
            ),
            IconButton(
              tooltip: 'Search',
              onPressed: onSearch,
              icon: const Icon(Icons.search, size: 21),
            ),
          ],
        ),
      );
      final account = island(
        context,
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            IconButton(
              key: const Key('compute-usage-button'),
              tooltip: 'Compute',
              onPressed: onCompute,
              icon: const Icon(Icons.speed_outlined, size: 18),
            ),
            if (!narrow)
              IconButton(
                tooltip: 'Settings',
                onPressed: onSettings,
                icon: const Icon(Icons.settings_outlined, size: 20),
              ),
            PopupMenuButton<String>(
              tooltip: 'Profile',
              position: PopupMenuPosition.over,
              icon: CircleAvatar(
                radius: 14,
                backgroundColor: Theme.of(context).colorScheme.primary
                    .withValues(alpha: .15),
                child: Icon(
                  Icons.person_outline,
                  size: 18,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
              onSelected: (value) {
                if (value == 'switch-account') {
                  onSwitchAccount?.call();
                  return;
                }
                if (value == 'settings') {
                  onSettings();
                  return;
                }
                if (value == 'dock') {
                  store.settings.dockTop = !store.settings.dockTop;
                }
                if (value == 'assistant') {
                  store.settings.assistantRight =
                      !store.settings.assistantRight;
                }
                store.save();
              },
              itemBuilder: (_) => [
                PopupMenuItem(
                  enabled: false,
                  child: Text(
                    store.settings.displayName.isEmpty
                        ? 'Personal account'
                        : store.settings.displayName,
                  ),
                ),
                const PopupMenuDivider(),
                if (onSwitchAccount != null)
                  const PopupMenuItem(
                    value: 'switch-account',
                    child: ListTile(
                      leading: Icon(Icons.switch_account_outlined),
                      title: Text('Switch account'),
                      contentPadding: EdgeInsets.zero,
                    ),
                  ),
                PopupMenuItem(
                  value: 'dock',
                  child: Text(
                    store.settings.dockTop
                        ? 'Move controls to bottom'
                        : 'Move controls to top',
                  ),
                ),
                PopupMenuItem(
                  value: 'assistant',
                  child: Text(
                    store.settings.assistantRight
                        ? 'Assistant on left'
                        : 'Assistant on right',
                  ),
                ),
                const PopupMenuItem(value: 'settings', child: Text('Settings')),
              ],
            ),
          ],
        ),
      );
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            workspace,
            Flexible(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                child: SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: assistantIsland,
                ),
              ),
            ),
            account,
          ],
        ),
      );
    },
  );
}
