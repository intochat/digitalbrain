import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'package:digitalbrain_flutter/widgets/neuron_vector_logo.dart';
import 'ui_form_scope.dart';

class UiKitButton extends StatelessWidget {
  final String label;
  final String pack;
  final String experienceId;
  final String eventName;
  final String synapseType;
  final String icon;
  final Map<String, Object?> eventProps;
  final RemoteEventHandler onEvent;

  const UiKitButton({
    required this.label,
    required this.pack,
    required this.experienceId,
    required this.eventName,
    required this.onEvent,
    this.synapseType = 'ExperienceStep',
    this.icon = '',
    this.eventProps = const {},
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
            ...eventProps,
            if (pack.isNotEmpty) 'pack': pack,
            if (experienceId.isNotEmpty) 'experienceId': experienceId,
            if (eventName.isNotEmpty) 'eventName': eventName,
            ...capturedValues,
          },
        });
      },
      child: _ButtonContent(label: label, icon: icon),
    );
  }
}

class _ButtonContent extends StatelessWidget {
  const _ButtonContent({required this.label, required this.icon});

  final String label;
  final String icon;

  @override
  Widget build(BuildContext context) {
    final iconName = icon.trim();
    if (iconName.isEmpty) {
      return Text(label);
    }

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        NeuronVectorLogo(neuronId: iconName, size: 16),
        const SizedBox(width: 8),
        Flexible(child: Text(label)),
      ],
    );
  }
}
