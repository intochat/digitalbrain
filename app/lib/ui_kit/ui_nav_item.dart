import 'package:rfw/rfw.dart' show RemoteEventHandler;

class UiNavItem {
  const UiNavItem(this.label, this.eventName);
  final String label;
  final String eventName;
}

List<UiNavItem> parseNavItems(List rawItems) => rawItems
    .cast<Map>()
    .map((m) => UiNavItem((m['label'] ?? '').toString(), (m['eventName'] ?? '').toString()))
    .toList();

void fireNav(
  RemoteEventHandler onEvent,
  String pack,
  String experienceId,
  String eventName,
  Map<String, String> capturedValues,
) {
  onEvent('press', {
    'synapseType': 'ExperienceStep',
    'props': {'pack': pack, 'experienceId': experienceId, 'eventName': eventName, ...capturedValues},
  });
}
