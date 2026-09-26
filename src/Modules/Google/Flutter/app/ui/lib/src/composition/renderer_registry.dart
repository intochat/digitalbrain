/// The one place that says how a catalog kind is drawn.
///
/// Every UI kind from `DigitalBrain.Flutter.UIVocabulary` and every semantic field kind from
/// `DigitalBrain.Contracts.Types.FieldKind` resolves here. A kind with a dedicated renderer
/// resolves to it; every other kind, including one the client has never seen, resolves to the
/// declared fallback, so a window is never blank.
enum RendererSurface { dedicated, fallback }

final class RendererEntry {
  const RendererEntry(this.kind, this.surface);

  final String kind;
  final RendererSurface surface;

  bool get isFallback => surface == RendererSurface.fallback;
}

final class RendererRegistry {
  const RendererRegistry._();

  static const fallback = 'unsupported';

  static const fallbackExplanation =
      'This kind has no dedicated renderer yet, so it is shown with the fallback.';

  static const fallbackLabel = 'Not supported yet';

  /// Mirrors `DigitalBrain.Flutter.UIVocabulary`.
  static const List<String> uiKinds = [
    'surface',
    'layout',
    'collection',
    'imagecanvas',
    'button',
    'toggle',
    'text',
    'textfield',
    'slider',
    'progress',
    'infobar',
    'card',
    'image',
    'video',
    'webbrowser',
    'chart',
    'graph',
    'table',
    'calendar',
    'clock',
    'map',
    'person',
    'rating',
    'color',
    'expander',
    'tabs',
    'tree',
    'sheet',
    'form',
  ];

  /// Kinds `NeuronView` draws itself. Everything else in [uiKinds] uses the fallback.
  static const Set<String> dedicatedUiKinds = {
    'surface',
    'layout',
    'collection',
    'imagecanvas',
    'button',
    'text',
    'textfield',
    'card',
    'tabs',
    'form',
  };

  /// Mirrors `DigitalBrain.Contracts.Types.FieldKind`.
  static const List<String> fieldKinds = [
    'PlainText',
    'LongText',
    'Number',
    'Date',
    'DateTime',
    'Boolean',
    'Choice',
    'MultiChoice',
    'Email',
    'Url',
    'Secret',
    'Reference',
  ];

  /// Field kinds the form renderer draws with a dedicated input. The rest fall back to plain text.
  static const Set<String> dedicatedFieldKinds = {
    'PlainText',
    'LongText',
    'Number',
    'Email',
    'Url',
    'Date',
    'Boolean',
    'Choice',
    'Secret',
  };

  static RendererEntry uiEntry(String kind) => RendererEntry(
    dedicatedUiKinds.contains(kind) ? kind : fallback,
    dedicatedUiKinds.contains(kind)
        ? RendererSurface.dedicated
        : RendererSurface.fallback,
  );

  static RendererEntry fieldEntry(String kind) => RendererEntry(
    dedicatedFieldKinds.contains(kind) ? kind : fallback,
    dedicatedFieldKinds.contains(kind)
        ? RendererSurface.dedicated
        : RendererSurface.fallback,
  );

  /// Every UI kind the server can address, registered or deliberately falling back.
  static Iterable<String> get uiFallbackKinds =>
      uiKinds.where((kind) => !dedicatedUiKinds.contains(kind));

  static Iterable<String> get fieldFallbackKinds =>
      fieldKinds.where((kind) => !dedicatedFieldKinds.contains(kind));
}
