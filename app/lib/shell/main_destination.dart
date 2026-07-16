import 'package:flutter/material.dart';

enum MainDestination {
  chat(
    label: 'Chat',
    location: '/chat',
    icon: Icons.chat_bubble_outline,
    selectedIcon: Icons.chat_bubble,
  ),
  features(
    label: 'Features',
    location: '/features',
    icon: Icons.auto_awesome_outlined,
    selectedIcon: Icons.auto_awesome,
  ),
  activity(
    label: 'Activity',
    location: '/activity',
    icon: Icons.history_outlined,
    selectedIcon: Icons.history,
  );

  const MainDestination({
    required this.label,
    required this.location,
    required this.icon,
    required this.selectedIcon,
  });

  final String label;
  final String location;
  final IconData icon;
  final IconData selectedIcon;

  static MainDestination? forLocation(Uri location) {
    for (final destination in values) {
      if (destination == MainDestination.features &&
          location.path.startsWith('/features/proposals/')) {
        continue;
      }
      if (location.path == destination.location ||
          location.path.startsWith('${destination.location}/')) {
        return destination;
      }
    }
    return null;
  }
}
