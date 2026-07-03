import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';

FTile buildExperienceFTile({
  required String title,
  required String subtitle,
  required String pack,
  required String experienceId,
  required String eventName,
  required RemoteEventHandler onEvent,
  Map<String, String> capturedValues = const {},
}) => FTile(
      title: Text(title),
      subtitle: subtitle.isEmpty ? null : Text(subtitle),
      onPress: eventName.isEmpty
          ? null
          : () => onEvent('press', {
                'synapseType': 'ExperienceStep',
                'props': {
                  'pack': pack,
                  'experienceId': experienceId,
                  'eventName': eventName,
                  ...capturedValues,
                },
              }),
    );

class UiKitTile extends StatelessWidget {
  const UiKitTile({
    super.key,
    required this.title,
    this.subtitle = '',
    this.pack = '',
    this.experienceId = '',
    this.eventName = '',
    this.onEvent,
  });
  final String title;
  final String subtitle;
  final String pack;
  final String experienceId;
  final String eventName;
  final RemoteEventHandler? onEvent;

  @override
  Widget build(BuildContext context) {
    final tile = onEvent == null
        ? FTile(
            title: Text(title),
            subtitle: subtitle.isEmpty ? null : Text(subtitle),
          )
        : buildExperienceFTile(
            title: title,
            subtitle: subtitle,
            pack: pack,
            experienceId: experienceId,
            eventName: eventName,
            onEvent: onEvent!,
            capturedValues: UiKitFormScope.of(context)?.values ?? const {},
          );
    // FTile requires a group context for layout constraints; wrap standalone tiles in FTileGroup.
    return FTileGroup(children: [tile]);
  }
}
