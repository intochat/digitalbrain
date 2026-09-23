import 'package:digitalbrain_ui/src/composition/renderer_registry.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('every catalog kind resolves to a renderer or a declared fallback', () {
    expect(RendererRegistry.uiKinds.length, 30);
    for (final kind in RendererRegistry.uiKinds) {
      final entry = RendererRegistry.uiEntry(kind);
      expect(entry.isFallback, !RendererRegistry.dedicatedUiKinds.contains(kind));
      expect(entry.kind.isEmpty, isFalse);
    }
    expect(RendererRegistry.fieldKinds.length, 12);
    for (final kind in RendererRegistry.fieldKinds) {
      final entry = RendererRegistry.fieldEntry(kind);
      expect(entry.isFallback, !RendererRegistry.dedicatedFieldKinds.contains(kind));
      expect(entry.kind.isEmpty, isFalse);
    }
  });

  test('kinds without a dedicated renderer are declared, not silent', () {
    expect(RendererRegistry.uiFallbackKinds, isNotEmpty);
    expect(RendererRegistry.fieldFallbackKinds, isNotEmpty);
    for (final kind in RendererRegistry.uiFallbackKinds) {
      expect(RendererRegistry.uiEntry(kind).kind, RendererRegistry.fallback);
      expect(
        RendererRegistry.uiEntry(kind).surface,
        RendererSurface.fallback,
      );
    }
  });

  test('unknown kind renders the fallback, never a blank', () {
    final unknown = RendererRegistry.uiEntry('this-kind-does-not-exist');
    expect(unknown.kind, RendererRegistry.fallback);
    expect(unknown.isFallback, isTrue);
    expect(RendererRegistry.fallbackExplanation, isNotEmpty);
    expect(RendererRegistry.fallbackLabel, isNotEmpty);
    expect(
      RendererRegistry.fieldEntry('not-a-field-kind').kind,
      RendererRegistry.fallback,
    );
  });

  test('dedicated UI and field kinds keep their own renderer', () {
    expect(RendererRegistry.uiEntry('card').surface, RendererSurface.dedicated);
    expect(RendererRegistry.uiEntry('form').surface, RendererSurface.dedicated);
    expect(RendererRegistry.fieldEntry('Date').surface, RendererSurface.dedicated);
    expect(RendererRegistry.fieldEntry('Secret').surface, RendererSurface.dedicated);
    expect(RendererRegistry.fieldEntry('Reference').surface, RendererSurface.fallback);
  });
}
