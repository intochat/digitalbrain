// Design-time catalog of the Tier-1 widget-canvas palette (docs/redesign/01:
// "Widgetbook catalogs the palette; RFW is the engine"). Each use-case renders a
// palette primitive through the real RFW runtime host from a tiny inline RFW
// document — the exact path the shipped app uses — so the catalog shows what the
// kernel-emitted surfaces actually render to. Manual directories, no codegen.
//
// Run with:  flutter run -t lib/widgetbook.dart
import 'package:flutter/material.dart';
// Dev-only catalog entrypoint; widgetbook is intentionally a dev_dependency.
// ignore: depend_on_referenced_packages
import 'package:widgetbook/widgetbook.dart';

import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

void main() => runApp(const PaletteWidgetbook());

class PaletteWidgetbook extends StatelessWidget {
  const PaletteWidgetbook({super.key});

  @override
  Widget build(BuildContext context) {
    return Widgetbook.material(
      directories: [
        WidgetbookCategory(
          name: 'Palette',
          children: [
            _component('LottiePlayer', {
              'Network animation':
                  'LottiePlayer(src: "https://assets10.lottiefiles.com/packages/lf20_pmYw5P.json", loop: true, autoplay: true, speed: 1.0)',
            }),
            _component('AnalogClock', {
              'Minimal face': 'AnalogClock(showSeconds: true, face: "minimal")',
              'Numerals face':
                  'AnalogClock(showSeconds: true, face: "numerals")',
            }),
            _component('CountdownClock', {
              'Two minutes, snoozable':
                  'CountdownClock(durationSeconds: 120, startedAtUtc: "", onSnooze: event "snooze" {})',
            }),
            _component('EarthGlobe', {
              'Route arc LHR → JFK':
                  'EarthGlobe(autoRotate: true, points: [{lat: 51.47, lng: -0.45, label: "LHR"}, {lat: 40.64, lng: -73.78, label: "JFK"}], arcs: [{from: {lat: 51.47, lng: -0.45}, to: {lat: 40.64, lng: -73.78}, style: "dashed"}])',
            }),
            _component('FloatingWindow', {
              'Busy lock':
                  'FloatingWindow(title: "Surface", lockState: "busy", child: Text(text: "Body content"))',
            }),
          ],
        ),
      ],
    );
  }

  WidgetbookComponent _component(String name, Map<String, String> useCases) {
    return WidgetbookComponent(
      name: name,
      useCases: [
        for (final entry in useCases.entries)
          WidgetbookUseCase(
            name: entry.key,
            builder: (context) => _RfwStage(source: _doc(entry.value)),
          ),
      ],
    );
  }

  String _doc(String widget) => 'import digitalbrain;\nwidget root = $widget;';
}

/// Renders one inline RFW document through the runtime host, centred on the
/// canvas background so themed primitives read against the real surface colour.
class _RfwStage extends StatefulWidget {
  const _RfwStage({required this.source});

  final String source;

  @override
  State<_RfwStage> createState() => _RfwStageState();
}

class _RfwStageState extends State<_RfwStage> {
  final RfwRuntimeHost _host = RfwRuntimeHost();
  static const String _key = 'widgetbook-stage';

  @override
  Widget build(BuildContext context) {
    _host.ensureLoaded(_key, widget.source);
    final err = _host.parseError(_key);
    return ColoredBox(
      color: const Color(0xFF0D0E12),
      child: Center(
        child: SizedBox(
          width: 360,
          height: 360,
          child: err != null
              ? Padding(
                  padding: const EdgeInsets.all(16),
                  child: Text(
                    'Surface error: $err',
                    style: TextStyle(color: DigitalBrainColors.rose),
                  ),
                )
              : _host.render(_key, data: const {}, onEvent: (_, _) {}),
        ),
      ),
    );
  }
}
