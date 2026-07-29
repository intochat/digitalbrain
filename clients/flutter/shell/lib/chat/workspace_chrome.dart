import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class WorkspaceRail extends StatelessWidget {
  const WorkspaceRail({
    super.key,
    required this.selectedIndex,
    required this.onSelected,
  });

  final int selectedIndex;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    return NavigationRail(
      backgroundColor: BrainPalette.navigation,
      minWidth: 88,
      groupAlignment: -0.78,
      labelType: NavigationRailLabelType.all,
      selectedIndex: selectedIndex,
      onDestinationSelected: onSelected,
      leading: const Padding(
        padding: EdgeInsets.only(top: 10, bottom: 28),
        child: BrainMark(),
      ),
      destinations: workspaceRailDestinations,
    );
  }
}

final class WorkspaceNavigationBar extends StatelessWidget {
  const WorkspaceNavigationBar({
    super.key,
    required this.selectedIndex,
    required this.onSelected,
  });

  final int selectedIndex;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    return NavigationBar(
      selectedIndex: selectedIndex,
      onDestinationSelected: onSelected,
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.forum_outlined, key: Key('destination_chat')),
          selectedIcon: Icon(Icons.forum, key: Key('destination_chat')),
          label: 'Chat',
        ),
        NavigationDestination(
          icon: Icon(Icons.timeline_outlined, key: Key('destination_activity')),
          selectedIcon: Icon(Icons.timeline, key: Key('destination_activity')),
          label: 'Activity',
        ),
        NavigationDestination(
          icon: Icon(Icons.hub_outlined, key: Key('destination_brain')),
          selectedIcon: Icon(Icons.hub, key: Key('destination_brain')),
          label: 'Brain',
        ),
      ],
    );
  }
}

const workspaceRailDestinations = <NavigationRailDestination>[
  NavigationRailDestination(
    icon: Icon(Icons.forum_outlined, key: Key('destination_chat')),
    selectedIcon: Icon(Icons.forum, key: Key('destination_chat')),
    label: Text('Chat'),
  ),
  NavigationRailDestination(
    icon: Icon(Icons.timeline_outlined, key: Key('destination_activity')),
    selectedIcon: Icon(Icons.timeline, key: Key('destination_activity')),
    label: Text('Activity'),
  ),
  NavigationRailDestination(
    icon: Icon(Icons.hub_outlined, key: Key('destination_brain')),
    selectedIcon: Icon(Icons.hub, key: Key('destination_brain')),
    label: Text('Brain'),
  ),
];

final class BrainMark extends StatelessWidget {
  const BrainMark({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 36,
      height: 36,
      decoration: BoxDecoration(
        color: BrainPalette.signal.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.signal.withValues(alpha: 0.4)),
      ),
      child: const Icon(
        Icons.graphic_eq_rounded,
        color: BrainPalette.signal,
        size: 20,
      ),
    );
  }
}

final class WorkspaceStatusBar extends StatelessWidget {
  const WorkspaceStatusBar({
    super.key,
    required this.chatName,
    required this.section,
    this.message,
  });

  final String chatName;
  final String section;
  final String? message;

  @override
  Widget build(BuildContext context) {
    final offline = message != null && message!.isNotEmpty;

    return Container(
      height: 58,
      padding: const EdgeInsets.symmetric(horizontal: 24),
      decoration: const BoxDecoration(
        color: BrainPalette.surfaceRaised,
        border: Border(bottom: BorderSide(color: BrainPalette.line)),
      ),
      child: Row(
        children: [
          const Text('DigitalBrain', style: BrainType.title),
          const SizedBox(width: 10),
          Container(width: 1, height: 16, color: BrainPalette.lineStrong),
          const SizedBox(width: 10),
          Text(section, style: BrainType.metaStrong),
          const Spacer(),
          Text('chat:$chatName', style: BrainType.meta),
          const SizedBox(width: 14),
          Container(
            width: 7,
            height: 7,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: offline ? BrainPalette.signal : BrainPalette.success,
              boxShadow: [
                BoxShadow(
                  color: (offline ? BrainPalette.signal : BrainPalette.success)
                      .withValues(alpha: 0.35),
                  blurRadius: 8,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(offline ? 'not connected' : 'connected', style: BrainType.meta),
        ],
      ),
    );
  }
}

String workspaceSectionName(int index) => switch (index) {
  0 => 'Chat',
  1 => 'Activity',
  _ => 'Brain',
};
