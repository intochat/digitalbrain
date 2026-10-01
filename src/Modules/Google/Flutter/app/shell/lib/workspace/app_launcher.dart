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

/// The launcher lists the shell's own module surfaces plus every declarative catalog app, which
/// installs through its consent sheet.
List<AppLauncherEntry> launcherEntries(List<AppManifestSummary> apps) {
  final entries = <AppLauncherEntry>[
    ...defaultLauncherEntries,
    for (final app in apps)
      if (app.kind == 'declarative') catalogLauncherEntry(app),
  ];
  entries.sort(
    (a, b) => a.title.toLowerCase().compareTo(b.title.toLowerCase()),
  );
  return entries;
}

/// A catalog app without a shell window: the manifest id is the launch key for its consent sheet.
AppLauncherEntry catalogLauncherEntry(AppManifestSummary app) =>
    AppLauncherEntry(
      launchKey: app.id,
      title: app.name,
      subtitle: app.description,
      icon: Icons.bolt_outlined,
    );

const defaultLauncherEntries = <AppLauncherEntry>[
  AppLauncherEntry(
    launchKey: 'files',
    title: 'Files',
    subtitle: 'Workspace assets',
    icon: Icons.folder_outlined,
  ),
  AppLauncherEntry(
    launchKey: 'images',
    title: 'Image Editor',
    subtitle: 'Draw, crop and export',
    icon: Icons.tune,
  ),
];

const csharpLauncherEntry = AppLauncherEntry(
  launchKey: 'csharp',
  title: 'C# files',
  subtitle: 'Write and run single-file C# apps',
  icon: Icons.code,
);

const assistantLauncherEntry = AppLauncherEntry(
  launchKey: 'assistant',
  title: 'Assistant',
  subtitle: 'Chat or talk with the assistant',
  icon: Icons.assistant_outlined,
);

const customerResearcherLauncherEntry = AppLauncherEntry(
  launchKey: 'customer-researcher',
  title: 'Customer Researcher',
  subtitle: 'Research companies live and save to Postgres',
  icon: Icons.travel_explore,
);
