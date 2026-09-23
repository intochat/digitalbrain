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

/// The launcher is driven by installed manifests. First-party apps keep their established window
/// keys and labels; only apps the shell can open today are listed. Saved declarative apps are listed
/// by their manifest id and opened through the server. An empty list falls back to the built-in
/// first-party entries.
List<AppLauncherEntry> launcherEntries(List<AppManifestSummary> apps) {
  if (apps.isEmpty) return defaultLauncherEntries;
  final entries = <AppLauncherEntry>[];
  for (final app in apps) {
    if (_firstPartyIds.contains(app.id)) {
      final entry = entryFor(app);
      if (_openableLaunchKeys.contains(entry.launchKey)) entries.add(entry);
    } else if (app.kind == 'declarative') {
      entries.add(savedLauncherEntry(app));
    }
  }
  entries.sort(
    (a, b) => a.title.toLowerCase().compareTo(b.title.toLowerCase()),
  );
  return entries;
}

const _openableLaunchKeys = {'files', 'images', 'mydata', 'behaviors'};

const _firstPartyIds = {
  'intochat.files',
  'intochat.image-editor',
  'intochat.mydata',
  'intochat.customer-tables',
  'intochat.forms',
};

/// A saved app: the manifest id is the launch key, because the server reopens its windows.
AppLauncherEntry savedLauncherEntry(AppManifestSummary app) => AppLauncherEntry(
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
  'intochat.mydata' => const AppLauncherEntry(
    launchKey: 'mydata',
    title: 'My Data',
    subtitle: 'Your facts and secrets',
    icon: Icons.shield_outlined,
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
  AppLauncherEntry(
    launchKey: 'mydata',
    title: 'My Data',
    subtitle: 'Your facts and secrets',
    icon: Icons.shield_outlined,
  ),
];

const behaviorsLauncherEntry = AppLauncherEntry(
  launchKey: 'behaviors',
  title: 'Behaviors',
  subtitle: 'Create and manage automations',
  icon: Icons.account_tree_outlined,
);
