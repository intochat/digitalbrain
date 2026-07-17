import 'package:flutter/material.dart';

import '../gateway/brain_gateway.dart';
import 'abilities_page.dart';
import 'activity_page.dart';
import 'chat_page.dart';
import 'connections_page.dart';
import 'inspector.dart';
import 'today_page.dart';

class _Destination {
  const _Destination(this.label, this.icon);

  final String label;
  final IconData icon;
}

const List<_Destination> _destinations = [
  _Destination('Today', Icons.today_outlined),
  _Destination('Chat', Icons.chat_bubble_outline),
  _Destination('Abilities', Icons.bolt_outlined),
  _Destination('Connections', Icons.hub_outlined),
  _Destination('Activity', Icons.timeline_outlined),
];

const double _wideBreakpoint = 700;
const double _inspectorWidth = 320;

class ShellState extends ChangeNotifier {
  int _destination = 0;
  int get destination => _destination;

  String? _inspectorAddress;
  String? get inspectorAddress => _inspectorAddress;

  bool _inspectorVisible = false;
  bool get inspectorVisible => _inspectorVisible;

  void selectDestination(int index) {
    if (_destination == index) return;
    _destination = index;
    notifyListeners();
  }

  void inspect(String? address) {
    _inspectorAddress = address;
    _inspectorVisible = address != null;
    notifyListeners();
  }

  void toggleInspector() {
    _inspectorVisible = !_inspectorVisible;
    notifyListeners();
  }
}

class ShellScope extends InheritedNotifier<ShellState> {
  const ShellScope({required ShellState state, required super.child, super.key})
    : super(notifier: state);

  static ShellState of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<ShellScope>();
    assert(scope != null, 'No ShellScope found in context');
    return scope!.notifier!;
  }
}

class AppShell extends StatefulWidget {
  const AppShell(this.gateway, {super.key});

  final BrainGateway gateway;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  final ShellState _state = ShellState();

  @override
  void dispose() {
    _state.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ShellScope(
      state: _state,
      child: AnimatedBuilder(
        animation: _state,
        builder: (context, _) {
          return LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= _wideBreakpoint;
              return wide ? _wideLayout(context) : _narrowLayout(context);
            },
          );
        },
      ),
    );
  }

  Widget _page(int index) {
    switch (index) {
      case 0:
        return TodayPage(widget.gateway);
      case 1:
        return const ChatPage();
      case 2:
        return AbilitiesPage(widget.gateway);
      case 3:
        return const ConnectionsPage();
      case 4:
        return ActivityPage(widget.gateway);
      default:
        return const SizedBox.shrink();
    }
  }

  Widget _wideLayout(BuildContext context) {
    return Scaffold(
      body: Row(
        children: [
          NavigationRail(
            selectedIndex: _state.destination,
            onDestinationSelected: _state.selectDestination,
            labelType: NavigationRailLabelType.all,
            trailing: Expanded(
              child: Align(
                alignment: Alignment.bottomCenter,
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: IconButton(
                    tooltip: 'Inspector',
                    icon: const Icon(Icons.info_outline),
                    onPressed: _state.toggleInspector,
                  ),
                ),
              ),
            ),
            destinations: _destinations
                .map(
                  (destination) => NavigationRailDestination(
                    icon: Icon(destination.icon),
                    label: Text(destination.label),
                  ),
                )
                .toList(),
          ),
          const VerticalDivider(width: 1),
          Expanded(child: _page(_state.destination)),
          if (_state.inspectorVisible) ...[
            const VerticalDivider(width: 1),
            SizedBox(
              width: _inspectorWidth,
              child: Inspector(widget.gateway, _state.inspectorAddress),
            ),
          ],
        ],
      ),
    );
  }

  Widget _narrowLayout(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(_destinations[_state.destination].label),
        actions: [
          IconButton(
            tooltip: 'Inspector',
            icon: const Icon(Icons.info_outline),
            onPressed: () => showModalBottomSheet<void>(
              context: context,
              builder: (_) => SizedBox(
                height: 320,
                child: Inspector(widget.gateway, _state.inspectorAddress),
              ),
            ),
          ),
        ],
      ),
      body: _page(_state.destination),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _state.destination,
        onDestinationSelected: _state.selectDestination,
        destinations: _destinations
            .map(
              (destination) => NavigationDestination(
                icon: Icon(destination.icon),
                label: destination.label,
              ),
            )
            .toList(),
      ),
    );
  }
}
