import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

/// A launcher row. [launchKey] is what the workspace store opens; manifest ids are not store keys.
class AppLauncherEntry {
  const AppLauncherEntry({
    required this.launchKey,
    required this.title,
    required this.subtitle,
    required this.icon,
  });

  final String launchKey;
  final String title;
  final String subtitle;
  final IconData icon;
}

/// The launcher is driven by catalog manifests. First-party apps keep their established window
/// keys and labels; only apps the shell can open today are listed. Other declarative apps are listed
/// by their manifest id and installed through their consent sheet. An empty list falls back to the
/// built-in first-party entries.
List<AppLauncherEntry> launcherEntries(List<AppManifestSummary> apps) {
  if (apps.isEmpty) return defaultLauncherEntries;
  final entries = <AppLauncherEntry>[];
  for (final app in apps) {
    if (_firstPartyIds.contains(app.id)) {
      final entry = entryFor(app);
      if (_openableLaunchKeys.contains(entry.launchKey)) entries.add(entry);
    } else if (app.kind == 'declarative') {
      entries.add(catalogLauncherEntry(app));
    }
  }
  entries.sort(
    (a, b) => a.title.toLowerCase().compareTo(b.title.toLowerCase()),
  );
  return entries;
}

const _openableLaunchKeys = {'files', 'images', 'behaviors'};

const _firstPartyIds = {
  'intochat.files',
  'intochat.image-editor',
  'intochat.customer-tables',
  'intochat.forms',
};

/// A catalog app without a shell window: the manifest id is the launch key for its consent sheet.
AppLauncherEntry catalogLauncherEntry(AppManifestSummary app) => AppLauncherEntry(
  launchKey: app.id,
  title: app.name,
  subtitle: app.description,
  icon: Icons.bolt_outlined,
);

AppLauncherEntry entryFor(AppManifestSummary app) => switch (app.id) {
  'intochat.files' => const AppLauncherEntry(
    launchKey: 'files',
    title: 'Files',
    subtitle: 'On this computer',
    icon: Icons.folder_outlined,
  ),
  'intochat.image-editor' => const AppLauncherEntry(
    launchKey: 'images',
    title: 'Image Editor',
    subtitle: 'Draw, crop and export',
    icon: Icons.tune,
  ),
  'intochat.customer-tables' => const AppLauncherEntry(
    launchKey: 'tables',
    title: 'Customer Tables',
    subtitle: 'Read and refine live tables',
    icon: Icons.table_chart_outlined,
  ),
  'intochat.forms' => const AppLauncherEntry(
    launchKey: 'forms',
    title: 'Forms',
    subtitle: 'Fill in a form',
    icon: Icons.edit_note,
  ),
  _ => AppLauncherEntry(
    launchKey: app.uiEntry ?? app.id,
    title: app.name,
    subtitle: app.description,
    icon: Icons.web_asset_outlined,
  ),
};

const defaultLauncherEntries = <AppLauncherEntry>[
  AppLauncherEntry(
    launchKey: 'files',
    title: 'Files',
    subtitle: 'On this computer',
    icon: Icons.folder_outlined,
  ),
  AppLauncherEntry(
    launchKey: 'images',
    title: 'Image Editor',
    subtitle: 'Draw, crop and export',
    icon: Icons.tune,
  ),
];

const behaviorsLauncherEntry = AppLauncherEntry(
  launchKey: 'behaviors',
  title: 'Behaviors',
  subtitle: 'Create and manage automations',
  icon: Icons.account_tree_outlined,
);
