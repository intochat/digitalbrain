import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

import 'rive_artboard.dart';
import 'rive_design_registry.dart';
import 'rive_handles.dart';

LocalWidgetLibrary createRiveWidgets(RiveDesignRegistry registry) {
  RiveArtboard build({
    required BuildContext context,
    required DataSource source,
    required String artboard,
    required Map<String, Object?> Function(DataSource) bindings,
    Map<String, VoidCallback?> Function(DataSource)? triggers,
    Map<String, AnimSpec?> Function(DataSource)? animSpecs,
  }) {
    return RiveArtboard(
      registry: registry,
      domain: source.v<String>(<Object>['domain']) ?? 'kernel',
      artboard: artboard,
      bindings: bindings(source),
      triggers: triggers?.call(source) ?? const <String, VoidCallback?>{},
      animSpecs: animSpecs?.call(source) ?? const <String, AnimSpec?>{},
    );
  }

  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'Hero': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Hero',
          bindings: (s) => {
            'title': s.v<String>(<Object>['title']),
            'subtitle': s.v<String>(<Object>['subtitle']),
            'mood': s.v<String>(<Object>['mood']),
            'accent': _color(s.v<int>(<Object>['accent'])),
          },
        ),
    'Tile': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Tile',
          bindings: (s) => {
            'kind': s.v<String>(<Object>['kind']),
            'line1': s.v<String>(<Object>['line1']),
            'line2': s.v<String>(<Object>['line2']),
            'line3': s.v<String>(<Object>['line3']),
            'accent': _color(s.v<int>(<Object>['accent'])),
          },
          triggers: (s) => {
            'tap': s.handler(<Object>['onTap'],
                (HandlerTrigger trigger) => trigger),
          },
        ),
    'Badge': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Badge',
          bindings: (s) => {
            'label': s.v<String>(<Object>['label']),
            // rfw DataSource.v only accepts int/double/bool/String; read as
            // double first, fall back to int (integer literal in rfwSource)
            'value0to1':
                s.v<double>(<Object>['value0to1']) ??
                s.v<int>(<Object>['value0to1']),
            'tone': _color(s.v<int>(<Object>['tone'])),
          },
          animSpecs: (s) => {
            'value0to1': animSpecFromBindings(
              durMs: s.v<int>(<Object>['value0to1AnimDurMs']),
              curve: s.v<String>(<Object>['value0to1AnimCurve']),
            ),
          },
        ),
    'PersonaInline': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'PersonaInline',
          bindings: (s) => {
            'mood': s.v<String>(<Object>['mood']),
            'energy':
                s.v<double>(<Object>['energy']) ??
                s.v<int>(<Object>['energy']),
          },
          triggers: (s) => {
            'pulse': s.handler(<Object>['onPulse'],
                (HandlerTrigger trigger) => trigger),
          },
          animSpecs: (s) => {
            'energy': animSpecFromBindings(
              durMs: s.v<int>(<Object>['energyAnimDurMs']),
              curve: s.v<String>(<Object>['energyAnimCurve']),
            ),
          },
        ),
    'Spacer': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Spacer',
          bindings: (s) => {
            'height':
                s.v<int>(<Object>['height']) ??
                s.v<double>(<Object>['height']),
            'motif': s.v<String>(<Object>['motif']),
          },
          animSpecs: (s) => {
            'height': animSpecFromBindings(
              durMs: s.v<int>(<Object>['heightAnimDurMs']),
              curve: s.v<String>(<Object>['heightAnimCurve']),
            ),
          },
        ),
  });
}

Color? _color(int? raw) => raw == null ? null : Color(raw);
