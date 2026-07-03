import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';
import 'ui_tile.dart';

class UiKitList extends StatelessWidget {
  const UiKitList({
    super.key,
    required this.tileDescriptors,
    required this.onEvent,
  });

  // Each entry is a raw child prop-map from the RFW node tree (keyed by 'props').
  final List<Map<String, Object?>> tileDescriptors;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    final captured = UiKitFormScope.of(context)?.values ?? const {};
    final tiles = tileDescriptors.map((child) {
      final cp = (child['props'] as Map<String, Object?>?) ?? const {};
      String cs(String key) => (cp[key] ?? '').toString();
      return buildExperienceFTile(
        title: cs('title'),
        subtitle: cs('subtitle'),
        pack: cs('pack'),
        experienceId: cs('experienceId'),
        eventName: cs('eventName'),
        onEvent: onEvent,
        capturedValues: captured,
      );
    }).toList();
    return FTileGroup(children: tiles);
  }
}
