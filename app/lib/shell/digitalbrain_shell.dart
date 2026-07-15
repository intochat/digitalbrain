import 'package:flutter/material.dart';

import '../digital_brain_ui/digital_brain_ui.dart';
import 'adaptive_navigation.dart';
import 'main_destination.dart';

export 'adaptive_navigation.dart' show digitalBrainSignOutButtonKey;

const Key digitalBrainOpenNavigationKey = Key('digitalbrain-open-navigation');
const Key digitalBrainCurrentContextKey = Key('digitalbrain-current-context');
const Key digitalBrainCanvasKey = Key('digitalbrain-canvas');

class DigitalBrainShell extends StatelessWidget {
  const DigitalBrainShell({
    super.key,
    required this.location,
    required this.onDestinationSelected,
    required this.onSignOut,
    required this.child,
  });

  final Uri location;
  final ValueChanged<MainDestination> onDestinationSelected;
  final VoidCallback onSignOut;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final size = WindowSizeContext.of(context);
    final compact = size == WindowSize.compact;
    final extended = size == WindowSize.large || size == WindowSize.xLarge;
    final destinations = MainDestination.values;
    final active = MainDestination.forLocation(location);
    final selectedIndex = active == null ? null : destinations.indexOf(active);
    final title = _contextTitle(active);

    return Scaffold(
      appBar: compact
          ? AppBar(
              leading: Builder(
                builder: (context) => IconButton(
                  key: digitalBrainOpenNavigationKey,
                  tooltip: 'Open navigation',
                  onPressed: () => Scaffold.of(context).openDrawer(),
                  icon: const Icon(Icons.menu),
                ),
              ),
              title: _ContextTitle(title: title),
              actions: [
                Semantics(
                  key: digitalBrainSignOutButtonKey,
                  label: 'Sign out',
                  button: true,
                  onTap: onSignOut,
                  excludeSemantics: true,
                  child: IconButton(
                    tooltip: 'Sign out',
                    onPressed: onSignOut,
                    icon: const Icon(Icons.logout),
                  ),
                ),
              ],
            )
          : null,
      drawer: compact
          ? AdaptiveNavigationDrawer(
              destinations: destinations,
              selectedIndex: selectedIndex,
              onDestinationSelected: onDestinationSelected,
            )
          : null,
      body: Row(
        children: [
          if (!compact)
            AdaptiveNavigationRail(
              destinations: destinations,
              selectedIndex: selectedIndex,
              extended: extended,
              onDestinationSelected: onDestinationSelected,
              onSignOut: onSignOut,
            ),
          Expanded(
            key: digitalBrainCanvasKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (!compact)
                  Material(
                    color: Theme.of(context).colorScheme.surface,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 24,
                        vertical: 18,
                      ),
                      child: _ContextTitle(title: title),
                    ),
                  ),
                Expanded(child: child),
              ],
            ),
          ),
        ],
      ),
    );
  }

  String _contextTitle(MainDestination? active) {
    if (location.path.startsWith('/features/proposals/')) {
      return 'Feature Studio';
    }
    return active?.label ?? 'DigitalBrain';
  }
}

class _ContextTitle extends StatelessWidget {
  const _ContextTitle({required this.title});

  final String title;

  @override
  Widget build(BuildContext context) => Semantics(
    key: digitalBrainCurrentContextKey,
    container: true,
    header: true,
    liveRegion: true,
    label: title,
    excludeSemantics: true,
    child: Text(title, style: Theme.of(context).textTheme.titleLarge),
  );
}
