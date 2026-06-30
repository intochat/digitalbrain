import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'ui_form_scope.dart';

class UiKitButton extends StatelessWidget {
  final String label;
  final String pack;
  final String experienceId;
  final String eventName;
  final String synapseType;
  final RemoteEventHandler onEvent;

  const UiKitButton({
    required this.label,
    required this.pack,
    required this.experienceId,
    required this.eventName,
    required this.onEvent,
    this.synapseType = 'ExperienceStep',
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final scope = UiKitFormScope.of(context);
    return FButton(
      onPress: () {
        final capturedValues = scope?.values ?? {};
        onEvent('press', {
          'synapseType': synapseType,
          'props': {
            'pack': pack,
            'experienceId': experienceId,
            'eventName': eventName,
            ...capturedValues,
          },
        });
      },
      child: Text(label),
    );
  }
}
