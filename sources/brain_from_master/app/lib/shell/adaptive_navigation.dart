import 'package:flutter/material.dart';

import 'main_destination.dart';

class AdaptiveNavigationRail extends StatelessWidget {
  const AdaptiveNavigationRail({
    super.key,
    required this.destinations,
    required this.selectedIndex,
    required this.extended,
    required this.onDestinationSelected,
    required this.onSignOut,
  });

  final List<MainDestination> destinations;
  final int? selectedIndex;
  final bool extended;
  final ValueChanged<MainDestination> onDestinationSelected;
  final VoidCallback onSignOut;

  @override
  Widget build(BuildContext context) => NavigationRail(
    extended: extended,
    labelType: NavigationRailLabelType.none,
    selectedIndex: selectedIndex,
    onDestinationSelected: (index) =>
        onDestinationSelected(destinations[index]),
    leading: Padding(
      padding: const EdgeInsets.symmetric(vertical: 16),
      child: extended
          ? Text('DigitalBrain', style: Theme.of(context).textTheme.titleLarge)
          : const Icon(Icons.hub_outlined),
    ),
    trailingAtBottom: true,
    trailing: Semantics(
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
    destinations: [
      for (final destination in destinations)
        NavigationRailDestination(
          icon: Tooltip(
            message: destination.label,
            child: Icon(destination.icon),
          ),
          selectedIcon: Tooltip(
            message: destination.label,
            child: Icon(destination.selectedIcon),
          ),
          label: Text(destination.label),
        ),
    ],
  );
}

class AdaptiveNavigationDrawer extends StatelessWidget {
  const AdaptiveNavigationDrawer({
    super.key,
    required this.destinations,
    required this.selectedIndex,
    required this.onDestinationSelected,
  });

  final List<MainDestination> destinations;
  final int? selectedIndex;
  final ValueChanged<MainDestination> onDestinationSelected;

  @override
  Widget build(BuildContext context) => NavigationDrawer(
    selectedIndex: selectedIndex,
    onDestinationSelected: (index) {
      Navigator.of(context).pop();
      onDestinationSelected(destinations[index]);
    },
    header: Padding(
      padding: const EdgeInsets.fromLTRB(28, 24, 28, 12),
      child: Text(
        'DigitalBrain',
        style: Theme.of(context).textTheme.titleLarge,
      ),
    ),
    children: [
      for (final destination in destinations)
        NavigationDrawerDestination(
          icon: Icon(destination.icon),
          selectedIcon: Icon(destination.selectedIcon),
          label: Text(destination.label),
        ),
    ],
  );
}

const Key digitalBrainSignOutButtonKey = Key('digitalbrain-sign-out-button');
