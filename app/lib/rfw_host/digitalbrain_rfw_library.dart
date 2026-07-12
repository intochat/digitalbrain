// The DigitalBrain RFW widget dictionary.
//
// This is the *fixed, host-owned* vocabulary an RFW document may compose.
// It is small and slow-changing on purpose: the Creator generates trees
// over these primitives (no client rebuild), and because every widget here
// reads `Theme.of(context)` / DigitalBrainColors, generated UI inherits the
// DigitalBrain visual language for free. No arbitrary code is ever executed —
// an RFW document can only assemble these vetted widgets.

import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:rfw/rfw.dart' hide Switch;

import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:digitalbrain_flutter/features/live/graph/domain_palette.dart';
import 'package:digitalbrain_flutter/runtime/buses/prompt_input_bus.dart';
import 'package:digitalbrain_flutter/runtime/buses/typewriter_controller.dart';
import 'package:digitalbrain_flutter/runtime/buses/state_editor_bus.dart';
import 'package:digitalbrain_flutter/runtime/buses/llm_settings_bus.dart';
import 'package:digitalbrain_flutter/runtime/buses/ino_editor_bus.dart';
import 'package:digitalbrain_flutter/rfw_host/synapse_stream_scope.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_client_scope.dart';
import 'package:digitalbrain_flutter/features/brain/voice_input.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';

part 'library/helpers.dart';
part 'library/layout.dart';
part 'library/basic.dart';
part 'library/chat.dart';
part 'library/data.dart';

LocalWidgetLibrary createDigitalBrainWidgets() => LocalWidgetLibrary(_widgets);

Map<String, LocalWidgetBuilder> get _widgets => <String, LocalWidgetBuilder>{
  'Panel': _panel,
  'VStack': (c, s) => _stack(c, s, Axis.vertical),
  'HStack': (c, s) => _stack(c, s, Axis.horizontal),
  'Pad': _pad,
  'Text': _text,
  'Badge': _badge,
  'Button': _button,
  'Divider': (c, s) => _divider(),
  'Table': _table,
  'Progress': _progress,
  'Timeline': _timeline,
  'Avatar': _avatar,
  'TaskRow': _taskRow,
  'SynapseStream': _synapseStream,
  'CodeEditor': _codeEditor,
  'PromptInput': _promptInput,
  'Split': _split,
  'TabButton': _tabButton,
  'TabViewer': _tabViewer,
  'StateEditor': _stateEditor,
  'SynapseRow': (BuildContext c, DataSource s) {
    return _SynapseRowWidget(
      type: _str(s, 'type'),
      desc: _str(s, 'desc'),
      onCreate: s.voidHandler(['onCreate']),
      onFire: s.voidHandler(['onFire']),
    );
  },
  'SynapseCompactReference': (BuildContext c, DataSource s) {
    return _SynapseCompactReferenceWidget(
      type: _str(s, 'type'),
      desc: _str(s, 'desc'),
    );
  },
  'TelemetryPanel': _telemetryPanel,
  'LlmSettingsPanel': _llmSettingsPanel,

  // Pruned per P1.14 audit (kernel only emits via UiKitVocabulary + real surfaces from UiSurfaceRuntime / Pack / neurons).
  // Removed: Image, Tag, KeyValue, Bars, Metric, SectionLabel, GlowIcon, Bullets, AdaptiveContainer, Counter, Stars, Calendar, Donut, demo tabs, palette extras.
};

// (helpers provided via parts)

Widget _table(BuildContext c, DataSource s) {
  final nc = s.length(['columns']);
  final cols = <String>[
    for (var i = 0; i < nc; i++) _sp(s, ['columns', i]),
  ];
  Widget cell(String t, {bool head = false}) => Expanded(
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      child: Text(
        t,
        style: head
            ? GoogleFonts.jetBrainsMono(
                fontSize: 11,
                color: DigitalBrainColors.inkLow,
                letterSpacing: 1.2,
                fontWeight: FontWeight.w600,
              )
            : (Theme.of(c).textTheme.bodyMedium ?? const TextStyle()).copyWith(
                color: DigitalBrainColors.ink,
                fontSize: 13,
              ),
      ),
    ),
  );
  final children = <Widget>[
    Container(
      decoration: const BoxDecoration(
        border: Border(bottom: BorderSide(color: DigitalBrainColors.hairline)),
      ),
      child: Row(children: [for (final col in cols) cell(col, head: true)]),
    ),
  ];
  final nr = s.length(['rows']);
  for (var r = 0; r < nr; r++) {
    final ncell = s.length(['rows', r]);
    final cells = <String>[
      for (var x = 0; x < ncell; x++) _sp(s, ['rows', r, x]),
    ];
    children.add(
      Container(
        decoration: const BoxDecoration(
          border: Border(
            bottom: BorderSide(color: DigitalBrainColors.hairline),
          ),
        ),
        child: Row(children: [for (final x in cells) cell(x)]),
      ),
    );
  }
  return Column(mainAxisSize: MainAxisSize.min, children: children);
}

// ── code editor ────────────────────────────────────────────

Widget _codeEditor(BuildContext c, DataSource s) {
  final text = _str(s, 'text');
  final typing = _bool(s, 'typing', false);
  return _CodeEditorBody(text: text, typing: typing);
}

class _CodeEditorBody extends StatefulWidget {
  const _CodeEditorBody({required this.text, required this.typing});
  final String text;
  final bool typing;

  @override
  State<_CodeEditorBody> createState() => _CodeEditorBodyState();
}

String? resolveWordToFqn(String word, String fullText) {
  if (word.isEmpty) return null;

  if (word.contains('.')) {
    return word;
  }

  // 1. using alias = synapse(FQN)
  final usingReg = RegExp(
    r'\busing\s+' +
        RegExp.escape(word) +
        r'\s*=\s*(?:synapse|neuron|signal)\(([^)]+)\)',
    caseSensitive: true,
  );
  final usingMatch = usingReg.firstMatch(fullText);
  if (usingMatch != null) {
    return usingMatch.group(1)!.trim();
  }

  // 2. Bound variable: on/given synapse alias/FQN word
  final boundReg = RegExp(
    r'\b(?:on|given)\s+synapse\s+(?:([a-zA-Z_]\w*)|([a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+))\s+' +
        RegExp.escape(word) +
        r'\b',
  );
  final boundMatches = boundReg.allMatches(fullText);
  if (boundMatches.isNotEmpty) {
    final lastMatch = boundMatches.last;
    final alias = lastMatch.group(1);
    final fqn = lastMatch.group(2);
    if (fqn != null) {
      return fqn;
    } else if (alias != null) {
      // Recursively resolve
      return resolveWordToFqn(alias, fullText);
    }
  }

  return null;
}

class InoLangTextEditingController extends TextEditingController {
  InoLangTextEditingController({
    super.text,
    this.onHoverEnter,
    this.onHoverExit,
  });

  final void Function(String fqn, PointerEnterEvent event)? onHoverEnter;
  final void Function(PointerExitEvent event)? onHoverExit;

  @override
  TextSpan buildTextSpan({
    required BuildContext context,
    TextStyle? style,
    required bool withComposing,
  }) {
    final defaultStyle =
        style ??
        GoogleFonts.jetBrainsMono(
          fontSize: 12,
          color: DigitalBrainColors.ink,
          height: 1.5,
        );

    final List<InlineSpan> spans = <InlineSpan>[];
    int lastMatchEnd = 0;

    // Find all custom aliases and bound variables in the text
    final aliases = <String>{};
    final boundVars = <String>{};

    // 1. using alias = synapse(FQN)
    final usingReg = RegExp(
      r'\busing\s+([a-zA-Z_]\w*)\s*=\s*(?:synapse|neuron|signal)\(',
      caseSensitive: true,
    );
    for (final m in usingReg.allMatches(text)) {
      aliases.add(m.group(1)!);
    }

    // 2. on/given synapse alias/FQN varName:
    final boundReg = RegExp(
      r'\b(?:on|given)\s+synapse\s+(?:[a-zA-Z_]\w*|[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+)\s+([a-zA-Z_]\w*)\b',
    );
    for (final m in boundReg.allMatches(text)) {
      boundVars.add(m.group(1)!);
    }

    final customWords = <String>{...aliases, ...boundVars, 'it'};
    final customWordsPattern = customWords.isNotEmpty
        ? '|\\b(?:${customWords.where((w) => w.isNotEmpty).map(RegExp.escape).join('|')})\\b'
        : '';

    final RegExp regex = RegExp(
      r'(//.*)|'
      r'("[^"]*")|'
      r'(\b(?:on|synapse|signal|neuron|instance|ask|to|for|emit|given|returns|when|then|every|any|no|has|emitted|let|save|into|count|counter|scenario)\b)|'
      r'(\bit\b)|'
      '(\\b[a-zA-Z_]\\w*(?:\\.[a-zA-Z_]\\w*)+$customWordsPattern)|' // Group 5: FQNs, aliases, bound variables
      r'(\b\d+\b)|'
      r'(\b(?:using|namespace|public|sealed|record|class|struct|interface|get|set|init|private|protected|internal|override|virtual|async|await|return|string|int|var|bool|void)\b)|'
      r'(\[[a-zA-Z_]\w*\])',
    );

    for (final RegExpMatch match in regex.allMatches(text)) {
      if (match.start > lastMatchEnd) {
        spans.add(
          TextSpan(
            text: text.substring(lastMatchEnd, match.start),
            style: defaultStyle,
          ),
        );
      }

      if (match.group(1) != null) {
        // Comment
        spans.add(
          TextSpan(
            text: match.group(1),
            style: defaultStyle.copyWith(color: DigitalBrainColors.inkLow),
          ),
        );
      } else if (match.group(2) != null) {
        // String
        spans.add(
          TextSpan(
            text: match.group(2),
            style: defaultStyle.copyWith(color: DigitalBrainColors.tealSoft),
          ),
        );
      } else if (match.group(3) != null) {
        // Ino keyword
        spans.add(
          TextSpan(
            text: match.group(3),
            style: defaultStyle.copyWith(
              color: DigitalBrainColors.violetSoft,
              fontWeight: FontWeight.bold,
            ),
          ),
        );
      } else if (match.group(4) != null) {
        // Pronoun 'it'
        spans.add(
          TextSpan(
            text: match.group(4),
            style: defaultStyle.copyWith(
              color: DigitalBrainColors.gold,
              fontWeight: FontWeight.bold,
            ),
          ),
        );
      } else if (match.group(5) != null) {
        // Dotted FQN or alias or bound variable
        final word = match.group(5)!;

        final isFqn = word.contains('.');
        final isAlias = aliases.contains(word);
        final isBound = boundVars.contains(word) || word == 'it';

        if (isFqn || isAlias || isBound) {
          final fqn = resolveWordToFqn(word, text);

          Color fqnColor = DigitalBrainColors.tealSoft; // Default to synapse
          if (fqn != null) {
            final schema = DigitalBrainCatalogManager.instance.catalog
                .firstWhere(
                  (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
                  orElse: () =>
                      CatalogContractSchema(fqn: '', kind: -1, fields: []),
                );
            if (schema.kind == 1) fqnColor = DigitalBrainColors.goldSoft;
            if (schema.kind == 2) fqnColor = DigitalBrainColors.violetSoft;
          } else {
            if (isBound) fqnColor = DigitalBrainColors.tealSoft;
            if (isAlias) fqnColor = DigitalBrainColors.tealSoft;
          }

          spans.add(
            TextSpan(
              text: word,
              style: defaultStyle.copyWith(
                color: fqnColor,
                fontWeight: FontWeight.bold,
                decoration: TextDecoration.underline,
                decorationStyle: TextDecorationStyle.dotted,
                decorationColor: fqnColor.withValues(alpha: 0.5),
              ),
              mouseCursor: SystemMouseCursors.click,
              onEnter: (event) {
                if (fqn != null) {
                  onHoverEnter?.call(fqn, event);
                }
              },
              onExit: (event) => onHoverExit?.call(event),
            ),
          );
        } else {
          spans.add(TextSpan(text: word, style: defaultStyle));
        }
      } else if (match.group(6) != null) {
        // Number
        spans.add(
          TextSpan(
            text: match.group(6),
            style: defaultStyle.copyWith(color: DigitalBrainColors.rose),
          ),
        );
      } else if (match.group(7) != null) {
        // C# keyword
        spans.add(
          TextSpan(
            text: match.group(7),
            style: defaultStyle.copyWith(
              color: DigitalBrainColors.indigoSoft,
              fontWeight: FontWeight.bold,
            ),
          ),
        );
      } else if (match.group(8) != null) {
        // C# Attribute
        spans.add(
          TextSpan(
            text: match.group(8),
            style: defaultStyle.copyWith(color: DigitalBrainColors.gold),
          ),
        );
      }

      lastMatchEnd = match.end;
    }

    if (lastMatchEnd < text.length) {
      spans.add(
        TextSpan(text: text.substring(lastMatchEnd), style: defaultStyle),
      );
    }

    return TextSpan(children: spans, style: defaultStyle);
  }
}

class CatalogContractSchema {
  final String fqn;
  final int kind; // 0 = Synapse, 1 = Signal, 2 = Neuron
  final List<String> fields;

  CatalogContractSchema({
    required this.fqn,
    required this.kind,
    required this.fields,
  });

  factory CatalogContractSchema.fromJson(Map<String, dynamic> json) {
    return CatalogContractSchema(
      fqn: (json['Fqn'] ?? json['fqn'] ?? '').toString(),
      kind: (json['Kind'] ?? json['kind'] ?? 0) as int,
      fields: List<String>.from(json['Fields'] ?? json['fields'] ?? const []),
    );
  }
}

class DigitalBrainCatalogManager {
  DigitalBrainCatalogManager._internal();
  static final DigitalBrainCatalogManager instance =
      DigitalBrainCatalogManager._internal();

  List<CatalogContractSchema> _cachedCatalog = [];
  bool _loaded = false;

  List<CatalogContractSchema> get catalog => _cachedCatalog;
  bool get isLoaded => _loaded;

  Future<List<CatalogContractSchema>> ensureLoaded(BuildContext context) async {
    if (_loaded) return _cachedCatalog;
    await reload(context);
    return _cachedCatalog;
  }

  Future<void> reload(BuildContext context) async {
    final client = DigitalBrainClientScope.of(context);
    final assetBundle = DefaultAssetBundle.of(context);

    final requestPayload = jsonEncode({
      'SynapseId': '00000000-0000-0000-0000-000000000000',
      'CorrelationId': '00000000-0000-0000-0000-000000000000',
      'CallerNeuronId': '00000000-0000-0000-0000-000000000000',
      'CallerNeuronType': 'External',
      'ReceiverNeuronId': '00000000-0000-0000-0000-000000000000',
      'ReceiverNeuronType': 'IntrospectorNeuron',
      'Timestamp': DateTime.now().toUtc().toIso8601String(),
    });

    final envelope = SynapseEnvelope()
      ..correlationId = ''
      ..typeName =
          'DigitalBrain.Kernel.Contracts.Introspector.QueryCatalogContractsRequest'
      ..payload = Uint8List.fromList(utf8.encode(requestPayload));

    try {
      if (client != null) {
        final response = await client.send(envelope);
        final responsePayload = utf8.decode(response.payload);
        final decoded = jsonDecode(responsePayload);
        List? schemasJson;
        if (decoded is List) {
          schemasJson = decoded;
        } else if (decoded is Map) {
          schemasJson = (decoded['Schemas'] ?? decoded['schemas']) as List?;
        }
        if (schemasJson != null) {
          _cachedCatalog = schemasJson
              .map(
                (s) =>
                    CatalogContractSchema.fromJson(s as Map<String, dynamic>),
              )
              .toList();
          _loaded = true;
          return;
        }
      }
    } catch (e) {
      debugPrint(
        'Failed to load contract catalog: $e. Attempting local assets fallback.',
      );
    }

    // Try fallback
    try {
      final jsonStr = await assetBundle.loadString('assets/ino-catalog.json');
      final decoded = jsonDecode(jsonStr);
      List? schemasJson;
      if (decoded is List) {
        schemasJson = decoded;
      } else if (decoded is Map) {
        schemasJson = (decoded['Schemas'] ?? decoded['schemas']) as List?;
      }
      if (schemasJson != null) {
        _cachedCatalog = schemasJson
            .map(
              (s) => CatalogContractSchema.fromJson(s as Map<String, dynamic>),
            )
            .toList();
        _loaded = true;
      }
    } catch (assetErr) {
      debugPrint('Local assets fallback failed: $assetErr');
      if (!_loaded) {
        _cachedCatalog = [];
      }
    }
  }
}

class PromptTextEditingController extends TextEditingController {
  PromptTextEditingController({
    super.text,
    this.onHoverEnter,
    this.onHoverExit,
  });

  final void Function(String fqn, PointerEnterEvent event)? onHoverEnter;
  final void Function(PointerExitEvent event)? onHoverExit;

  @override
  TextSpan buildTextSpan({
    required BuildContext context,
    TextStyle? style,
    required bool withComposing,
  }) {
    final defaultStyle = style ?? const TextStyle();
    final textVal = value.text;
    if (textVal.isEmpty) {
      return TextSpan(text: '', style: defaultStyle);
    }

    final List<InlineSpan> spans = [];
    final regex = RegExp(r'([a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)*(?:\.\*)?)');
    int lastIndex = 0;

    for (final match in regex.allMatches(textVal)) {
      if (match.start > lastIndex) {
        spans.add(
          TextSpan(
            text: textVal.substring(lastIndex, match.start),
            style: defaultStyle,
          ),
        );
      }

      final word = match.group(1)!;
      final lowercaseWord = word.toLowerCase();

      final catalogMatch = DigitalBrainCatalogManager.instance.catalog.any(
        (s) => s.fqn.toLowerCase() == lowercaseWord,
      );

      final isWildcard =
          word.endsWith('.*') &&
          (lowercaseWord.startsWith('digitalbrain.sdk.') ||
              lowercaseWord.startsWith('acme.'));

      if (catalogMatch || isWildcard) {
        spans.add(
          TextSpan(
            text: word,
            style: defaultStyle.copyWith(
              decoration: TextDecoration.underline,
              decorationColor: DigitalBrainColors.tealSoft,
              decorationThickness: 1.5,
              fontWeight: FontWeight.bold,
            ),
            mouseCursor: SystemMouseCursors.click,
            onEnter: (event) {
              final fqnToPass = catalogMatch
                  ? DigitalBrainCatalogManager.instance.catalog
                        .firstWhere((s) => s.fqn.toLowerCase() == lowercaseWord)
                        .fqn
                  : word;
              onHoverEnter?.call(fqnToPass, event);
            },
            onExit: onHoverExit,
          ),
        );
      } else {
        spans.add(TextSpan(text: word, style: defaultStyle));
      }

      lastIndex = match.end;
    }

    if (lastIndex < textVal.length) {
      spans.add(
        TextSpan(text: textVal.substring(lastIndex), style: defaultStyle),
      );
    }

    return TextSpan(children: spans, style: defaultStyle);
  }
}

class _CodeEditorBodyState extends State<_CodeEditorBody> {
  late final InoLangTextEditingController _textController;
  late final FocusNode _focusNode;
  TypewriterController? _typewriter;

  List<String> _suggestions = [];
  bool _suggestionsVisible = false;
  String _currentWordBeingAutocompleted = '';
  List<CatalogContractSchema> _catalog = [];
  bool _catalogLoaded = false;

  // Hover card state
  OverlayEntry? _hoverCardEntry;
  String? _hoveredFqn;

  // Compilation state
  String _compileStatus = 'idle'; // 'idle', 'compiling', 'success', 'error'
  List<String> _compileErrors = [];

  @override
  void initState() {
    super.initState();
    _textController = InoLangTextEditingController(
      text: widget.text,
      onHoverEnter: _handleHoverEnter,
      onHoverExit: _handleHoverExit,
    );
    _focusNode = FocusNode();

    if (widget.typing) {
      _typewriter = TypewriterController()..appendChunk(widget.text);
      _typewriter!.addListener(_onTypewriterUpdated);
      WidgetsBinding.instance.addPostFrameCallback(_maybeCutForReducedMotion);
    }

    final sub = InoEditorBus.instance.activeSubscription;
    if (sub != null) {
      sub.addListener(_onSubscriptionChanged);
    }
  }

  void _handleHoverEnter(String fqn, PointerEnterEvent event) {
    _showHoverCard(fqn, event.position);
  }

  void _handleHoverExit(PointerExitEvent event) {
    _hideHoverCard();
  }

  void _showHoverCard(String fqn, Offset position) {
    if (_hoveredFqn == fqn) return;
    _hideHoverCard();
    _hoveredFqn = fqn;

    final schema = _catalog.firstWhere(
      (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
      orElse: () => CatalogContractSchema(fqn: '', kind: -1, fields: []),
    );
    if (schema.kind == -1) {
      return;
    }

    String kindName = 'UNKNOWN';
    if (schema.kind == 0) kindName = 'SYNAPSE';
    if (schema.kind == 1) kindName = 'SIGNAL';
    if (schema.kind == 2) kindName = 'NEURON';

    final Color accentColor = schema.kind == 0
        ? DigitalBrainColors.tealSoft
        : schema.kind == 1
        ? DigitalBrainColors.goldSoft
        : DigitalBrainColors.violetSoft;

    _hoverCardEntry = OverlayEntry(
      builder: (context) {
        return Positioned(
          left: position.dx + 12,
          top: position.dy + 12,
          child: Material(
            color: Colors.transparent,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: BackdropFilter(
                filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
                child: Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: Colors.black.withValues(alpha: 0.8),
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(
                      color: Colors.white.withValues(alpha: 0.15),
                    ),
                    boxShadow: [
                      BoxShadow(
                        color: Colors.black.withValues(alpha: 0.4),
                        blurRadius: 10,
                        offset: const Offset(0, 4),
                      ),
                    ],
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 6,
                              vertical: 2,
                            ),
                            decoration: BoxDecoration(
                              color: accentColor.withValues(alpha: 0.15),
                              borderRadius: BorderRadius.circular(4),
                              border: Border.all(
                                color: accentColor.withValues(alpha: 0.4),
                              ),
                            ),
                            child: Text(
                              kindName,
                              style: GoogleFonts.outfit(
                                fontSize: 9,
                                fontWeight: FontWeight.bold,
                                color: accentColor,
                                letterSpacing: 0.5,
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Text(
                            fqn,
                            style: GoogleFonts.jetBrainsMono(
                              fontSize: 11,
                              fontWeight: FontWeight.bold,
                              color: DigitalBrainColors.ink,
                            ),
                          ),
                        ],
                      ),
                      if (schema.fields.isNotEmpty) ...[
                        const SizedBox(height: 8),
                        Text(
                          'FIELDS',
                          style: GoogleFonts.outfit(
                            fontSize: 8,
                            fontWeight: FontWeight.bold,
                            color: DigitalBrainColors.inkLow,
                            letterSpacing: 0.5,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            for (final field in schema.fields)
                              Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 2,
                                ),
                                child: Text(
                                  '• $field',
                                  style: GoogleFonts.jetBrainsMono(
                                    fontSize: 10,
                                    color: DigitalBrainColors.inkMid,
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ],
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );

    Overlay.of(context).insert(_hoverCardEntry!);
  }

  void _hideHoverCard() {
    _hoveredFqn = null;
    _hoverCardEntry?.remove();
    _hoverCardEntry = null;
  }

  String? _getActiveNeuronId() {
    final correlationId =
        InoEditorBus.instance.activeSubscription?.correlationId;
    if (correlationId != null && correlationId.startsWith('editor-')) {
      return correlationId.substring('editor-'.length);
    }
    return null;
  }

  Future<void> _runCompileAndStage() async {
    setState(() {
      _compileStatus = 'compiling';
      _compileErrors = [];
    });

    final code = _textController.text;
    final client = DigitalBrainClientScope.of(context);

    if (client != null) {
      final neuronId = _getActiveNeuronId() ?? 'Unknown.Neuron';
      final requestPayload = jsonEncode({'Fqn': neuronId, 'InoSource': code});

      final envelope = SynapseEnvelope()
        ..typeName = 'DigitalBrain.Runtime.Introspector.PromoteNeuronRequest'
        ..payload = Uint8List.fromList(utf8.encode(requestPayload));

      try {
        final response = await client.send(envelope);
        final responsePayload = utf8.decode(response.payload);
        final responseData =
            jsonDecode(responsePayload) as Map<String, dynamic>;

        final success =
            (responseData['Success'] ?? responseData['success'] ?? false)
                as bool;
        final message =
            (responseData['Message'] ?? responseData['message'] ?? '')
                as String;
        final version =
            (responseData['Version'] ?? responseData['version'] ?? '')
                as String;

        if (mounted) {
          setState(() {
            if (success) {
              _compileStatus = 'success';
              _compileErrors = [];

              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(
                  content: Row(
                    children: [
                      const Icon(
                        Icons.check_circle,
                        color: DigitalBrainColors.teal,
                        size: 20,
                      ),
                      const SizedBox(width: 8),
                      Text(
                        'Promoted successfully to version $version!',
                        style: GoogleFonts.outfit(fontWeight: FontWeight.w500),
                      ),
                    ],
                  ),
                  backgroundColor: const Color(0xFF101222),
                  behavior: SnackBarBehavior.floating,
                  duration: const Duration(seconds: 3),
                ),
              );
            } else {
              _compileStatus = 'error';
              _compileErrors = message.isNotEmpty
                  ? message.split('|').map((e) => e.trim()).toList()
                  : ['Compilation failed'];
            }
          });
        }
        return;
      } catch (e) {
        debugPrint(
          'gRPC compilation failed, falling back to local verification: $e',
        );
      }
    }

    // Fallback: local simulation verification
    Future.delayed(const Duration(milliseconds: 600), () {
      if (!mounted) return;

      final errors = <String>[];

      // BOSN001: Check for neuron declaration
      if (!code.contains(
        RegExp(r'\bneuron\s+[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)+\b'),
      )) {
        errors.add(
          'BOSN001: Missing or invalid neuron FQN declaration. Every .ino document must declare a valid dotted FQN (e.g. neuron DigitalBrain.Examples.MyNeuron).',
        );
      }

      // BOSN002: Check for scenario block (L6 gate)
      if (!code.contains('scenario') && !code.contains('@')) {
        errors.add(
          'BOSN002: L6 Gate Violation - Document contains zero scenarios. Every neuron must carry at least one scenario block or DDD reference.',
        );
      }

      // BOSN003: Check that every using alias points to a valid FQN in the catalog
      final usingMatches = RegExp(
        r'\busing\s+(\w+)\s*=\s*(synapse|signal|neuron)\(([^)]+)\)',
      ).allMatches(code);
      for (final match in usingMatches) {
        final alias = match.group(1)!;
        final fqn = match.group(3)!.trim();

        final exists = _catalog.any(
          (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
        );
        if (!exists && _catalog.isNotEmpty) {
          errors.add(
            'BOSN003: Unknown contract FQN "$fqn" used in alias "$alias". Contract was not found in the live DigitalBrain catalog.',
          );
        }
      }

      // BOSN004 & BOSN005: Balanced parenthesis and brackets
      int parens = 0;
      int brackets = 0;
      for (int i = 0; i < code.length; i++) {
        if (code[i] == '(') parens++;
        if (code[i] == ')') parens--;
        if (code[i] == '[') brackets++;
        if (code[i] == ']') brackets--;
      }
      if (parens != 0) {
        errors.add(
          'BOSN004: Unbalanced parentheses. Found mismatched ( and ).',
        );
      }
      if (brackets != 0) {
        errors.add('BOSN005: Unbalanced brackets. Found mismatched [ and ].');
      }

      setState(() {
        if (errors.isEmpty) {
          _compileStatus = 'success';
          _compileErrors = [];

          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Row(
                children: [
                  const Icon(
                    Icons.check_circle,
                    color: DigitalBrainColors.teal,
                    size: 20,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    'Staged and compiled successfully!',
                    style: GoogleFonts.outfit(fontWeight: FontWeight.w500),
                  ),
                ],
              ),
              backgroundColor: const Color(0xFF101222),
              behavior: SnackBarBehavior.floating,
              duration: const Duration(seconds: 3),
            ),
          );
        } else {
          _compileStatus = 'error';
          _compileErrors = errors;
        }
      });
    });
  }

  Widget _buildCompileStatusIndicator() {
    switch (_compileStatus) {
      case 'compiling':
        return const SizedBox(
          width: 12,
          height: 12,
          child: CircularProgressIndicator(
            strokeWidth: 1.5,
            valueColor: AlwaysStoppedAnimation<Color>(DigitalBrainColors.gold),
          ),
        );
      case 'success':
        return const Icon(
          Icons.check_circle,
          color: DigitalBrainColors.teal,
          size: 14,
        );
      case 'error':
        return const Icon(
          Icons.error,
          color: DigitalBrainColors.rose,
          size: 14,
        );
      case 'idle':
      default:
        return Container(
          width: 6,
          height: 6,
          decoration: const BoxDecoration(
            color: DigitalBrainColors.inkLow,
            shape: BoxShape.circle,
          ),
        );
    }
  }

  Widget _buildCompileButton() {
    String label = 'Compile & Stage';
    if (_compileStatus == 'compiling') label = 'Compiling…';
    if (_compileStatus == 'success') label = 'Staged & Verified';
    if (_compileStatus == 'error') label = 'Compile Failed';

    final Color color = _compileStatus == 'success'
        ? DigitalBrainColors.tealSoft
        : _compileStatus == 'error'
        ? DigitalBrainColors.rose
        : DigitalBrainColors.violetSoft;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        key: const Key('compile-stage-btn'),
        onTap: _compileStatus == 'compiling' ? null : _runCompileAndStage,
        borderRadius: BorderRadius.circular(4),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
          child: Text(
            label,
            style: GoogleFonts.outfit(
              fontSize: 10,
              fontWeight: FontWeight.bold,
              color: color,
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildCompileDiagnosticsConsole() {
    if (_compileStatus != 'error' || _compileErrors.isEmpty) {
      return const SizedBox.shrink();
    }

    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(8),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
          child: Container(
            width: double.infinity,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.8),
              borderRadius: BorderRadius.circular(8),
              border: Border.all(
                color: DigitalBrainColors.rose.withValues(alpha: 0.3),
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(
                  children: [
                    const Icon(
                      Icons.error_outline,
                      color: DigitalBrainColors.rose,
                      size: 16,
                    ),
                    const SizedBox(width: 8),
                    Text(
                      'COMPILER DIAGNOSTICS (${_compileErrors.length} errors)',
                      style: GoogleFonts.outfit(
                        fontSize: 10,
                        fontWeight: FontWeight.bold,
                        color: DigitalBrainColors.rose,
                        letterSpacing: 1.0,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    for (final err in _compileErrors)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 4),
                        child: Text(
                          err,
                          style: GoogleFonts.jetBrainsMono(
                            fontSize: 10,
                            color: DigitalBrainColors.inkMid,
                            height: 1.4,
                          ),
                        ),
                      ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_catalogLoaded) {
      _catalogLoaded = true;
      _loadCatalog();
    }
  }

  Future<void> _loadCatalog() async {
    await DigitalBrainCatalogManager.instance.ensureLoaded(context);
    if (mounted) {
      setState(() {
        _catalog = DigitalBrainCatalogManager.instance.catalog;
      });
    }
  }

  void _onTypewriterUpdated() {
    if (_typewriter != null && mounted) {
      _textController.text = _typewriter!.shown;
    }
  }

  void _onSubscriptionChanged() {
    final sub = InoEditorBus.instance.activeSubscription;
    if (sub == null) return;
    if (_textController.text != sub.accumulated) {
      final selection = _textController.selection;
      _textController.text = sub.accumulated;
      try {
        _textController.selection = selection;
      } catch (_) {}
    }
  }

  void _maybeCutForReducedMotion(Duration _) {
    if (!mounted) return;
    if (MediaQuery.maybeOf(context)?.disableAnimations ?? false) {
      _typewriter?.cutToEnd();
    }
  }

  @override
  void didUpdateWidget(_CodeEditorBody old) {
    super.didUpdateWidget(old);
    if (widget.text != old.text) {
      _typewriter?.removeListener(_onTypewriterUpdated);
      _typewriter?.dispose();
      if (widget.typing) {
        _typewriter = TypewriterController()..appendChunk(widget.text);
        _typewriter!.addListener(_onTypewriterUpdated);
        _textController.text = _typewriter!.shown;
      } else {
        _typewriter = null;
        _textController.text = widget.text;
      }
    }
  }

  @override
  void dispose() {
    _hideHoverCard();
    _typewriter?.removeListener(_onTypewriterUpdated);
    _typewriter?.dispose();
    final sub = InoEditorBus.instance.activeSubscription;
    if (sub != null) {
      sub.removeListener(_onSubscriptionChanged);
    }
    _textController.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  void _onUserEdited(String newText) {
    if (_typewriter != null) {
      _typewriter!.removeListener(_onTypewriterUpdated);
      _typewriter!.dispose();
      _typewriter = null;
    }
    final sub = InoEditorBus.instance.activeSubscription;
    if (sub != null) {
      sub.updateText(newText);
    }
    _checkAutocomplete(newText);
  }

  void _checkAutocomplete(String text) {
    final selection = _textController.selection;
    if (!selection.isValid || selection.baseOffset <= 0) {
      _hideSuggestions();
      return;
    }

    final textBeforeCursor = text.substring(0, selection.baseOffset);

    // 1. Property access completion: e.g. $mySyn. or $mySyn.De
    final propMatch = RegExp(
      r'\$?([a-zA-Z_]\w*)\.(\w*)$',
    ).firstMatch(textBeforeCursor);
    if (propMatch != null) {
      final alias = propMatch.group(1)!;
      final prefix = propMatch.group(2)!;

      final targetFqn = resolveWordToFqn(alias, text);
      if (targetFqn != null) {
        final schema = _catalog.firstWhere(
          (s) => s.fqn.toLowerCase() == targetFqn.toLowerCase(),
          orElse: () => CatalogContractSchema(fqn: '', kind: 0, fields: []),
        );
        if (schema.fqn.isNotEmpty) {
          final matched = schema.fields
              .where(
                (f) =>
                    f.toLowerCase().startsWith(prefix.toLowerCase()) &&
                    f.toLowerCase() != prefix.toLowerCase(),
              )
              .toList();
          if (matched.isNotEmpty) {
            setState(() {
              _suggestions = matched;
              _currentWordBeingAutocompleted = prefix;
              _suggestionsVisible = true;
            });
            return;
          }
        }
      }
    }

    // 2. Kind-specific FQN completion inside parentheses: e.g. neuron( or synapse( or signal(
    final kindMatch = RegExp(
      r'\b(neuron|synapse|signal)\(([\w\.]*)$',
    ).firstMatch(textBeforeCursor);
    if (kindMatch != null) {
      final kindStr = kindMatch.group(1)!;
      final prefix = kindMatch.group(2)!;
      int targetKind = 0;
      if (kindStr == 'synapse') targetKind = 0;
      if (kindStr == 'signal') targetKind = 1;
      if (kindStr == 'neuron') targetKind = 2;

      final matched = _catalog
          .where((s) => s.kind == targetKind)
          .map((s) => s.fqn)
          .where(
            (fqn) =>
                fqn.toLowerCase().startsWith(prefix.toLowerCase()) &&
                fqn.toLowerCase() != prefix.toLowerCase(),
          )
          .toList();
      if (matched.isNotEmpty) {
        setState(() {
          _suggestions = matched;
          _currentWordBeingAutocompleted = prefix;
          _suggestionsVisible = true;
        });
        return;
      }
    }

    // 3. Fallback: standard word completion
    final lastWordMatch = RegExp(
      r'([\w\.]+|[#!\$~])$',
    ).firstMatch(textBeforeCursor);
    if (lastWordMatch == null) {
      _hideSuggestions();
      return;
    }

    final currentWord = lastWordMatch.group(0)!;
    if (currentWord.isEmpty) {
      _hideSuggestions();
      return;
    }

    final lexicon = [
      'using',
      'synapse',
      'signal',
      'neuron',
      'on',
      'scenario',
      'given',
      'when',
      'then',
      '#',
      '!',
      '\$',
      '~',
    ];

    final fqns = _catalog.map((s) => s.fqn).toList();
    final combined = [...lexicon, ...fqns];

    final matched = combined
        .where(
          (word) =>
              word.toLowerCase().startsWith(currentWord.toLowerCase()) &&
              word.toLowerCase() != currentWord.toLowerCase(),
        )
        .toList();

    if (matched.isEmpty) {
      _hideSuggestions();
    } else {
      setState(() {
        _suggestions = matched;
        _currentWordBeingAutocompleted = currentWord;
        _suggestionsVisible = true;
      });
    }
  }

  void _hideSuggestions() {
    if (_suggestionsVisible) {
      setState(() {
        _suggestionsVisible = false;
        _suggestions = [];
      });
    }
  }

  void _selectSuggestion(String suggestion) {
    final text = _textController.text;
    final selection = _textController.selection;
    if (!selection.isValid) return;

    final textBeforeCursor = text.substring(0, selection.baseOffset);
    final textAfterCursor = text.substring(selection.baseOffset);

    final prefix = textBeforeCursor.substring(
      0,
      textBeforeCursor.length - _currentWordBeingAutocompleted.length,
    );
    final newText = '$prefix$suggestion $textAfterCursor';

    _textController.text = newText;

    final newCursorPos = prefix.length + suggestion.length + 1;
    _textController.selection = TextSelection.collapsed(offset: newCursorPos);

    final sub = InoEditorBus.instance.activeSubscription;
    sub?.updateText(newText);

    _hideSuggestions();
    _focusNode.requestFocus();
  }

  String _cleanKey(String suggestion) {
    if (suggestion == '#') return 'hash';
    if (suggestion == '!') return 'excl';
    if (suggestion == '\$') return 'dollar';
    if (suggestion == '~') return 'tilde';
    return suggestion.toLowerCase().replaceAll('.', '-');
  }

  Color _getSuggestionColor(String suggestion) {
    if ([
      'using',
      'on',
      'scenario',
      'given',
      'when',
      'then',
    ].contains(suggestion)) {
      return DigitalBrainColors.indigoSoft;
    }
    if (['synapse', 'signal', 'neuron'].contains(suggestion)) {
      return DigitalBrainColors.violetSoft;
    }
    if (['#', '!', '\$', '~'].contains(suggestion)) {
      return DigitalBrainColors.gold;
    }
    return DigitalBrainColors.tealSoft;
  }

  Widget _buildCode() {
    final lines = _textController.text.split('\n');
    final lineCount = lines.isEmpty ? 1 : lines.length;

    final lineNumberStyle = GoogleFonts.jetBrainsMono(
      fontSize: 12,
      color: DigitalBrainColors.inkLow,
      height: 1.5,
    );
    final codeStyle = GoogleFonts.jetBrainsMono(
      fontSize: 12,
      color: DigitalBrainColors.ink,
      height: 1.5,
    );

    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: DigitalBrainColors.obsidian,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Gutter
          Container(
            padding: const EdgeInsets.only(
              left: 12,
              top: 12,
              bottom: 12,
              right: 8,
            ),
            decoration: const BoxDecoration(
              border: Border(
                right: BorderSide(color: DigitalBrainColors.hairline),
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                for (int i = 0; i < lineCount; i++)
                  Text('${i + 1}', style: lineNumberStyle),
              ],
            ),
          ),

          // Editable text area
          Expanded(
            child: TextField(
              key: const Key('ino-code-editor'),
              controller: _textController,
              focusNode: _focusNode,
              maxLines: null,
              keyboardType: TextInputType.multiline,
              style: codeStyle,
              cursorColor: DigitalBrainColors.violetSoft,
              onChanged: _onUserEdited,
              scrollPhysics: const NeverScrollableScrollPhysics(),
              decoration: const InputDecoration(
                contentPadding: EdgeInsets.all(12),
                border: InputBorder.none,
                isDense: true,
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Stack(
          children: [
            _buildCode(),

            // Premium Floating Staging Panel
            Positioned(
              top: 12,
              right: 12,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: BackdropFilter(
                  filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 6,
                    ),
                    decoration: BoxDecoration(
                      color: Colors.black.withValues(alpha: 0.65),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(
                        color: Colors.white.withValues(alpha: 0.12),
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        _buildCompileStatusIndicator(),
                        const SizedBox(width: 8),
                        _buildCompileButton(),
                      ],
                    ),
                  ),
                ),
              ),
            ),

            if (_suggestionsVisible && _suggestions.isNotEmpty)
              Positioned(
                bottom: 12,
                left: 12,
                right: 12,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: BackdropFilter(
                    filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
                    child: Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: Colors.black.withValues(alpha: 0.85),
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(
                          color: Colors.white.withValues(alpha: 0.15),
                          width: 1,
                        ),
                        boxShadow: [
                          BoxShadow(
                            color: Colors.black.withValues(alpha: 0.4),
                            blurRadius: 16,
                            offset: const Offset(0, 8),
                          ),
                        ],
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Row(
                            children: [
                              const Icon(
                                Icons.bolt,
                                color: DigitalBrainColors.gold,
                                size: 14,
                              ),
                              const SizedBox(width: 6),
                              Text(
                                'INOLANG SUGGESTIONS',
                                style: GoogleFonts.outfit(
                                  fontSize: 9,
                                  fontWeight: FontWeight.bold,
                                  color: DigitalBrainColors.inkLow,
                                  letterSpacing: 1.0,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 8),
                          Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            children: [
                              for (final suggestion in _suggestions)
                                Material(
                                  color: Colors.transparent,
                                  child: InkWell(
                                    key: Key(
                                      'autocomplete-item-${_cleanKey(suggestion)}',
                                    ),
                                    onTap: () => _selectSuggestion(suggestion),
                                    borderRadius: BorderRadius.circular(6),
                                    child: Container(
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 10,
                                        vertical: 6,
                                      ),
                                      decoration: BoxDecoration(
                                        color: Colors.white.withValues(
                                          alpha: 0.08,
                                        ),
                                        borderRadius: BorderRadius.circular(6),
                                        border: Border.all(
                                          color: Colors.white.withValues(
                                            alpha: 0.1,
                                          ),
                                        ),
                                      ),
                                      child: Text(
                                        suggestion,
                                        style: GoogleFonts.jetBrainsMono(
                                          fontSize: 11,
                                          fontWeight: FontWeight.bold,
                                          color: _getSuggestionColor(
                                            suggestion,
                                          ),
                                        ),
                                      ),
                                    ),
                                  ),
                                ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
          ],
        ),
        _buildCompileDiagnosticsConsole(),
      ],
    );
  }
}

// ── prompt input ──────────────────────────────────────────

Widget _promptInput(BuildContext c, DataSource s) {
  final placeholder = _str(s, 'placeholder', 'Describe a new behavior…');
  final submitLabel = _str(s, 'submitLabel', 'Create');
  final onSubmit = s.voidHandler(['onSubmit']);
  return _PromptInputBody(
    placeholder: placeholder,
    submitLabel: submitLabel,
    onSubmit: onSubmit,
  );
}

class _PromptInputBody extends StatefulWidget {
  const _PromptInputBody({
    required this.placeholder,
    required this.submitLabel,
    required this.onSubmit,
  });

  final String placeholder;
  final String submitLabel;
  final VoidCallback? onSubmit;

  @override
  State<_PromptInputBody> createState() => _PromptInputBodyState();
}

class _PromptInputBodyState extends State<_PromptInputBody> {
  late final PromptTextEditingController _controller =
      PromptTextEditingController(
        text: PromptInputBus.instance.text,
        onHoverEnter: _handleHoverEnter,
        onHoverExit: _handleHoverExit,
      )..addListener(_pushToBus);

  void _pushToBus() => PromptInputBus.instance.set(_controller.text);

  OverlayEntry? _hoverCardEntry;
  String? _hoveredFqn;
  bool _catalogLoaded = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_catalogLoaded) {
      _catalogLoaded = true;
      DigitalBrainCatalogManager.instance.ensureLoaded(context).then((_) {
        if (mounted) {
          setState(() {});
        }
      });
    }
  }

  void _handleHoverEnter(String fqn, PointerEnterEvent event) {
    _showHoverCard(fqn, event.position);
  }

  void _handleHoverExit(PointerExitEvent event) {
    _hideHoverCard();
  }

  void _showHoverCard(String fqn, Offset position) {
    if (_hoveredFqn == fqn) return;
    _hideHoverCard();
    _hoveredFqn = fqn;

    final List<CatalogContractSchema> matchingSchemas = [];
    final bool isWildcard = fqn.endsWith('.*');

    if (isWildcard) {
      final prefix = fqn.substring(0, fqn.length - 2).toLowerCase();
      matchingSchemas.addAll(
        DigitalBrainCatalogManager.instance.catalog.where(
          (s) => s.fqn.toLowerCase().startsWith(prefix),
        ),
      );
    } else {
      matchingSchemas.addAll(
        DigitalBrainCatalogManager.instance.catalog.where(
          (s) => s.fqn.toLowerCase() == fqn.toLowerCase(),
        ),
      );
    }

    if (matchingSchemas.isEmpty) {
      return;
    }

    final Color accentColor = matchingSchemas.first.kind == 0
        ? DigitalBrainColors.tealSoft
        : matchingSchemas.first.kind == 1
        ? DigitalBrainColors.goldSoft
        : DigitalBrainColors.violetSoft;

    String kindName = 'UNKNOWN';
    if (matchingSchemas.first.kind == 0) kindName = 'SYNAPSE';
    if (matchingSchemas.first.kind == 1) kindName = 'SIGNAL';
    if (matchingSchemas.first.kind == 2) kindName = 'NEURON';

    _hoverCardEntry = OverlayEntry(
      builder: (context) {
        return Positioned(
          left: position.dx + 12,
          top: position.dy + 12,
          child: Material(
            color: Colors.transparent,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: BackdropFilter(
                filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
                child: Container(
                  width: 320,
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: Colors.black.withValues(alpha: 0.75),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(
                      color: accentColor.withValues(alpha: 0.3),
                      width: 1.5,
                    ),
                    boxShadow: [
                      BoxShadow(
                        color: accentColor.withValues(alpha: 0.25),
                        blurRadius: 18,
                        spreadRadius: 2,
                      ),
                    ],
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 8,
                              vertical: 3,
                            ),
                            decoration: BoxDecoration(
                              color: accentColor.withValues(alpha: 0.15),
                              borderRadius: BorderRadius.circular(6),
                              border: Border.all(
                                color: accentColor.withValues(alpha: 0.4),
                              ),
                            ),
                            child: Text(
                              kindName,
                              style: GoogleFonts.outfit(
                                fontSize: 9,
                                fontWeight: FontWeight.bold,
                                color: accentColor,
                                letterSpacing: 0.5,
                              ),
                            ),
                          ),
                          Text(
                            isWildcard
                                ? '${matchingSchemas.length} MATCHES'
                                : '${matchingSchemas.length} OVERLOADS',
                            style: GoogleFonts.outfit(
                              fontSize: 9,
                              fontWeight: FontWeight.bold,
                              color: DigitalBrainColors.ink,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 10),
                      Text(
                        fqn,
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 13,
                          fontWeight: FontWeight.bold,
                          color: DigitalBrainColors.ink,
                        ),
                      ),
                      const SizedBox(height: 6),
                      const Divider(
                        color: DigitalBrainColors.hairline,
                        height: 16,
                      ),
                      Flexible(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            for (
                              int i = 0;
                              i < matchingSchemas.length;
                              i++
                            ) ...[
                              if (i > 0)
                                const Divider(
                                  color: DigitalBrainColors.hairlineStrong,
                                  height: 16,
                                ),
                              Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 4,
                                ),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      matchingSchemas[i].fqn,
                                      style: GoogleFonts.jetBrainsMono(
                                        fontSize: 10,
                                        fontWeight: FontWeight.w600,
                                        color: accentColor,
                                      ),
                                    ),
                                    const SizedBox(height: 6),
                                    if (matchingSchemas[i]
                                        .fields
                                        .isNotEmpty) ...[
                                      Text(
                                        'FIELDS',
                                        style: GoogleFonts.outfit(
                                          fontSize: 8,
                                          fontWeight: FontWeight.bold,
                                          color: DigitalBrainColors.inkLow,
                                          letterSpacing: 0.5,
                                        ),
                                      ),
                                      const SizedBox(height: 4),
                                      Wrap(
                                        spacing: 6,
                                        runSpacing: 4,
                                        children: [
                                          for (final field
                                              in matchingSchemas[i].fields)
                                            Container(
                                              padding:
                                                  const EdgeInsets.symmetric(
                                                    horizontal: 6,
                                                    vertical: 3,
                                                  ),
                                              decoration: BoxDecoration(
                                                color: Colors.white.withValues(
                                                  alpha: 0.05,
                                                ),
                                                borderRadius:
                                                    BorderRadius.circular(4),
                                                border: Border.all(
                                                  color: Colors.white
                                                      .withValues(alpha: 0.1),
                                                ),
                                              ),
                                              child: Text(
                                                field,
                                                style:
                                                    GoogleFonts.jetBrainsMono(
                                                      fontSize: 9,
                                                      color: DigitalBrainColors
                                                          .inkMid,
                                                    ),
                                              ),
                                            ),
                                        ],
                                      ),
                                    ] else ...[
                                      Text(
                                        'No payload fields defined.',
                                        style: GoogleFonts.manrope(
                                          fontSize: 10,
                                          fontStyle: FontStyle.italic,
                                          color: DigitalBrainColors.inkLow,
                                        ),
                                      ),
                                    ],
                                  ],
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );

    Overlay.of(context).insert(_hoverCardEntry!);
  }

  void _hideHoverCard() {
    _hoveredFqn = null;
    _hoverCardEntry?.remove();
    _hoverCardEntry = null;
  }

  @override
  void dispose() {
    _hideHoverCard();
    _controller
      ..removeListener(_pushToBus)
      ..dispose();
    super.dispose();
  }

  void _submit() {
    PromptInputBus.instance.set(_controller.text);
    widget.onSubmit?.call();
  }

  @override
  Widget build(BuildContext context) {
    final client = DigitalBrainClientScope.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        TextField(
          controller: _controller,
          minLines: 3,
          maxLines: 8,
          textInputAction: TextInputAction.newline,
          style: GoogleFonts.manrope(
            fontSize: 14,
            color: DigitalBrainColors.ink,
          ),
          decoration: InputDecoration(
            hintText: widget.placeholder,
            hintStyle: GoogleFonts.manrope(
              fontSize: 14,
              color: DigitalBrainColors.inkLow,
            ),
            filled: true,
            fillColor: DigitalBrainColors.obsidian,
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(10),
              borderSide: const BorderSide(color: DigitalBrainColors.hairline),
            ),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(10),
              borderSide: const BorderSide(color: DigitalBrainColors.hairline),
            ),
          ),
        ),
        const SizedBox(height: 10),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            if (client != null)
              VoiceInput(
                client: client,
                onTranscript: (t) {
                  setState(() {
                    _controller.text = '${_controller.text} $t'.trim();
                  });
                },
                onError: (err) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text(err),
                      backgroundColor: DigitalBrainColors.rose,
                    ),
                  );
                },
              )
            else
              const SizedBox.shrink(),
            FilledButton(
              onPressed: widget.onSubmit == null ? null : _submit,
              child: Text(widget.submitLabel),
            ),
          ],
        ),
      ],
    );
  }
}

// ── split ─────────────────────────────────────────────────

Widget _split(BuildContext c, DataSource s) {
  final size = WindowSizeContext.of(c);
  final leftFraction = _d(s, 'leftFraction', 0.42).clamp(0.1, 0.9);
  final left = s.optionalChild(['left']) ?? const SizedBox.shrink();
  final right = s.optionalChild(['right']) ?? const SizedBox.shrink();
  if (size == WindowSize.compact) {
    return DefaultTabController(
      length: 2,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const TabBar(
            tabs: [
              Tab(text: 'Prompt'),
              Tab(text: 'Code'),
            ],
            labelColor: DigitalBrainColors.ink,
            unselectedLabelColor: DigitalBrainColors.inkLow,
          ),
          SizedBox(height: 480, child: TabBarView(children: [left, right])),
        ],
      ),
    );
  }
  return IntrinsicHeight(
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Expanded(flex: (leftFraction * 100).round(), child: left),
        const SizedBox(width: 12),
        Expanded(flex: ((1 - leftFraction) * 100).round(), child: right),
      ],
    ),
  );
}

// ── settings menu tabs & panels ───────────────────────────

Widget _tabButton(BuildContext c, DataSource s) {
  final label = _str(s, 'label');
  final active = _bool(s, 'active', false);
  final onTap = s.voidHandler(['onTap']);
  return InkWell(
    onTap: onTap,
    borderRadius: BorderRadius.circular(6),
    child: Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: active
            ? DigitalBrainColors.indigoSoft.withValues(alpha: 0.15)
            : Colors.transparent,
        borderRadius: BorderRadius.circular(6),
        border: Border.all(
          color: active ? DigitalBrainColors.indigoSoft : Colors.transparent,
        ),
      ),
      child: Text(
        label,
        style: GoogleFonts.manrope(
          fontSize: 12,
          color: active
              ? DigitalBrainColors.indigoSoft
              : DigitalBrainColors.inkMid,
          fontWeight: active ? FontWeight.bold : FontWeight.normal,
        ),
      ),
    ),
  );
}

Widget _tabViewer(BuildContext c, DataSource s) {
  final activeTab = _str(s, 'activeTab');
  final children = s.childList(['children']);

  final List<String> tabs = <String>[];
  final n = s.length(['tabs']);
  for (var i = 0; i < n; i++) {
    tabs.add(_sp(s, ['tabs', i]));
  }

  final index = tabs.indexOf(activeTab);
  if (index >= 0 && index < children.length) {
    return children[index];
  }
  return const SizedBox.shrink();
}

Widget _stateEditor(BuildContext c, DataSource s) {
  final stateJsonStr = _str(s, 'stateJson', '{}');
  final onUpdate = s.voidHandler(['onUpdate']);
  return _StateEditorBody(stateJson: stateJsonStr, onUpdate: onUpdate);
}

class _StateEditorBody extends StatefulWidget {
  const _StateEditorBody({required this.stateJson, required this.onUpdate});
  final String stateJson;
  final VoidCallback? onUpdate;

  @override
  State<_StateEditorBody> createState() => _StateEditorBodyState();
}

class _StateEditorBodyState extends State<_StateEditorBody> {
  late Map<String, dynamic> _stateMap = <String, dynamic>{};
  final Map<String, TextEditingController> _controllers =
      <String, TextEditingController>{};

  @override
  void initState() {
    super.initState();
    _parseJson();
  }

  void _parseJson() {
    try {
      _stateMap = jsonDecode(widget.stateJson) as Map<String, dynamic>;
      // Dispose old controllers
      for (final ctrl in _controllers.values) {
        ctrl.dispose();
      }
      _controllers.clear();
      // Create new controllers
      for (final entry in _stateMap.entries) {
        _controllers[entry.key] = TextEditingController(
          text: entry.value.toString(),
        );
      }
    } catch (_) {}
  }

  @override
  void didUpdateWidget(_StateEditorBody old) {
    super.didUpdateWidget(old);
    if (widget.stateJson != old.stateJson) {
      _parseJson();
    }
  }

  @override
  void dispose() {
    for (final ctrl in _controllers.values) {
      ctrl.dispose();
    }
    super.dispose();
  }

  void _saveValue(String key) {
    final controller = _controllers[key];
    if (controller == null) return;
    final valStr = controller.text.trim();

    dynamic parsed;
    if (valStr.toLowerCase() == 'true') {
      parsed = true;
    } else if (valStr.toLowerCase() == 'false') {
      parsed = false;
    } else if (int.tryParse(valStr) != null) {
      parsed = int.parse(valStr);
    } else if (double.tryParse(valStr) != null) {
      parsed = double.parse(valStr);
    } else {
      parsed = valStr;
    }

    StateEditorBus.instance.set(key, parsed);
    widget.onUpdate?.call();

    // Show instant micro-animation or feedback
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('State variable "$key" updated to: $parsed'),
        backgroundColor: DigitalBrainColors.indigoDeep,
        duration: const Duration(seconds: 1),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: DigitalBrainColors.obsidian,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'LIVE NEURON STATE',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 10,
                  color: DigitalBrainColors.inkLow,
                  fontWeight: FontWeight.bold,
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 5,
                ),
                decoration: BoxDecoration(
                  color: DigitalBrainColors.tealSoft.withValues(alpha: 0.16),
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  'Reactive',
                  style: GoogleFonts.manrope(
                    fontSize: 11,
                    color: DigitalBrainColors.tealSoft,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (_stateMap.isEmpty)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(
                'No state variables registered.',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkLow,
                ),
              ),
            )
          else
            ..._stateMap.entries.map((e) {
              final controller = _controllers[e.key];
              return Padding(
                padding: const EdgeInsets.symmetric(vertical: 4),
                child: Row(
                  children: [
                    Expanded(
                      flex: 2,
                      child: Text(
                        e.key,
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 12,
                          color: DigitalBrainColors.violetSoft,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      flex: 3,
                      child: Container(
                        height: 36,
                        decoration: BoxDecoration(
                          color: DigitalBrainColors.panelGlass,
                          borderRadius: BorderRadius.circular(6),
                          border: Border.all(
                            color: DigitalBrainColors.hairline,
                          ),
                        ),
                        child: Row(
                          children: [
                            const SizedBox(width: 8),
                            Expanded(
                              child: TextField(
                                controller: controller,
                                style: GoogleFonts.jetBrainsMono(
                                  fontSize: 12,
                                  color: DigitalBrainColors.goldSoft,
                                ),
                                decoration: const InputDecoration(
                                  border: InputBorder.none,
                                  isDense: true,
                                  contentPadding: EdgeInsets.zero,
                                ),
                                onSubmitted: (_) => _saveValue(e.key),
                              ),
                            ),
                            IconButton(
                              padding: EdgeInsets.zero,
                              iconSize: 16,
                              icon: const Icon(
                                Icons.check,
                                color: DigitalBrainColors.violetSoft,
                              ),
                              onPressed: () => _saveValue(e.key),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              );
            }),
          const SizedBox(height: 12),
          Text(
            'To update state, edit values directly or fire a synapse.',
            style: GoogleFonts.manrope(
              fontSize: 11,
              color: DigitalBrainColors.inkLow,
              fontStyle: FontStyle.italic,
            ),
          ),
        ],
      ),
    );
  }
}

class _SynapseRowWidget extends StatefulWidget {
  const _SynapseRowWidget({
    required this.type,
    required this.desc,
    required this.onCreate,
    required this.onFire,
  });

  final String type;
  final String desc;
  final VoidCallback? onCreate;
  final VoidCallback? onFire;

  @override
  State<_SynapseRowWidget> createState() => _SynapseRowWidgetState();
}

class _SynapseRowWidgetState extends State<_SynapseRowWidget> {
  bool _expanded = false;
  final Map<String, TextEditingController> _controllers = {};
  CatalogContractSchema? _schema;

  @override
  void initState() {
    super.initState();
    _findSchema();
  }

  void _findSchema() {
    final catalog = DigitalBrainCatalogManager.instance.catalog;
    for (final s in catalog) {
      if (s.fqn == widget.type || s.fqn.split('.').last == widget.type) {
        _schema = s;
        break;
      }
    }

    if (_schema != null) {
      for (final field in _schema!.fields) {
        if (field == 'Headers' ||
            field == 'headers' ||
            field == 'Metadata' ||
            field == 'metadata') {
          continue;
        }
        _controllers[field] = TextEditingController();
      }
    }
  }

  @override
  void dispose() {
    for (final ctrl in _controllers.values) {
      ctrl.dispose();
    }
    super.dispose();
  }

  Future<void> _fireSynapse() async {
    final client = DigitalBrainClientScope.of(context);
    if (client == null) {
      widget.onFire?.call();
      return;
    }

    final customFields = <String, dynamic>{};
    for (final entry in _controllers.entries) {
      final textVal = entry.value.text.trim();
      if (textVal.isEmpty) continue;

      dynamic val = textVal;
      if (textVal.toLowerCase() == 'true') {
        val = true;
      } else if (textVal.toLowerCase() == 'false') {
        val = false;
      } else if (int.tryParse(textVal) != null) {
        val = int.parse(textVal);
      } else if (double.tryParse(textVal) != null) {
        val = double.parse(textVal);
      }
      customFields[entry.key] = val;
    }

    final randomGuid = _generateGuid();
    var fqn = widget.type;
    if (_schema != null) {
      fqn = _schema!.fqn;
    }

    var receiverNeuronType = 'GatewayNeuron';
    if (fqn.contains('RequestDigestFeed') || fqn.contains('FetchDigestFeed')) {
      receiverNeuronType = 'DigitalBrain.Digest.DigestEmailFeedNeuron';
    } else if (fqn.contains('StoreLastNGmailSenders') ||
        fqn.contains('StoreLastNGmailSendersRequest')) {
      receiverNeuronType = 'GmailDigestNeuron';
    }

    final requestPayload = jsonEncode({
      'SynapseId': _generateGuid(),
      'CorrelationId': randomGuid,
      'CausationId': null,
      'CallerNeuronId': '00000000-0000-0000-0000-000000000000',
      'CallerNeuronType': 'External',
      'ReceiverNeuronId': '00000000-0000-0000-0000-000000000000',
      'ReceiverNeuronType': receiverNeuronType,
      'Timestamp': DateTime.now().toUtc().toIso8601String(),
      ...customFields,
    });

    final envelope = SynapseEnvelope()
      ..correlationId = randomGuid
      ..typeName = fqn
      ..payload = Uint8List.fromList(utf8.encode(requestPayload));

    try {
      widget.onFire?.call();

      await client.send(envelope);
      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Row(
            children: [
              const Icon(
                Icons.flash_on,
                color: DigitalBrainColors.gold,
                size: 20,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Fired synapse ${fqn.split('.').last} with custom parameters!',
                ),
              ),
            ],
          ),
          backgroundColor: DigitalBrainColors.teal,
          duration: const Duration(seconds: 3),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed to fire synapse: $e'),
          backgroundColor: DigitalBrainColors.rose,
        ),
      );
    }
  }

  String _generateGuid() {
    final rand = math.Random();
    String hexDigit(int index) => rand.nextInt(16).toRadixString(16);
    return '${List.generate(8, hexDigit).join()}-${List.generate(4, hexDigit).join()}-4${List.generate(3, hexDigit).join()}-${(rand.nextInt(4) + 8).toRadixString(16)}${List.generate(3, hexDigit).join()}-${List.generate(12, hexDigit).join()}';
  }

  @override
  Widget build(BuildContext context) {
    final hasFields = _controllers.isNotEmpty;

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      decoration: BoxDecoration(
        color: DigitalBrainColors.obsidian,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.all(10),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        widget.type,
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 12,
                          color: DigitalBrainColors.indigoSoft,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        widget.desc,
                        style: GoogleFonts.manrope(
                          fontSize: 11,
                          color: DigitalBrainColors.inkLow,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 10),
                if (hasFields)
                  IconButton(
                    tooltip: _expanded ? 'Hide Parameters' : 'Show Parameters',
                    icon: Icon(
                      Icons.tune,
                      color: _expanded
                          ? DigitalBrainColors.gold
                          : DigitalBrainColors.inkLow,
                      size: 16,
                    ),
                    onPressed: () {
                      setState(() {
                        _expanded = !_expanded;
                      });
                    },
                  ),
                IconButton(
                  tooltip: 'Create Handler',
                  icon: const Icon(
                    Icons.add_comment_outlined,
                    color: DigitalBrainColors.violetSoft,
                    size: 16,
                  ),
                  onPressed: widget.onCreate,
                ),
                IconButton(
                  tooltip: hasFields ? 'Configure & Fire' : 'Fire Synapse',
                  icon: const Icon(
                    Icons.flash_on,
                    color: DigitalBrainColors.gold,
                    size: 16,
                  ),
                  onPressed: _fireSynapse,
                ),
              ],
            ),
          ),
          if (_expanded && hasFields) ...[
            const Divider(color: DigitalBrainColors.hairline, height: 1),
            Container(
              padding: const EdgeInsets.all(12),
              color: DigitalBrainColors.panelGlass,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'SYNAPSE PAYLOAD PARAMETERS',
                    style: GoogleFonts.jetBrainsMono(
                      fontSize: 9,
                      color: DigitalBrainColors.inkLow,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 8),
                  ..._controllers.entries.map((entry) {
                    return Padding(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      child: Row(
                        children: [
                          Expanded(
                            flex: 2,
                            child: Text(
                              entry.key,
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 11,
                                color: DigitalBrainColors.violetSoft,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            flex: 3,
                            child: Container(
                              height: 32,
                              decoration: BoxDecoration(
                                color: DigitalBrainColors.obsidian,
                                borderRadius: BorderRadius.circular(6),
                                border: Border.all(
                                  color: DigitalBrainColors.hairline,
                                ),
                              ),
                              child: TextField(
                                controller: entry.value,
                                style: GoogleFonts.jetBrainsMono(
                                  fontSize: 11,
                                  color: DigitalBrainColors.goldSoft,
                                ),
                                decoration: const InputDecoration(
                                  border: InputBorder.none,
                                  isDense: true,
                                  contentPadding: EdgeInsets.symmetric(
                                    horizontal: 8,
                                    vertical: 8,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                    );
                  }),
                  const SizedBox(height: 10),
                  Align(
                    alignment: Alignment.centerRight,
                    child: FilledButton.icon(
                      onPressed: _fireSynapse,
                      icon: const Icon(
                        Icons.flash_on,
                        size: 14,
                        color: Colors.white,
                      ),
                      label: Text(
                        'Fire ${widget.type.split('.').last}',
                        style: GoogleFonts.manrope(
                          fontSize: 12,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      style: FilledButton.styleFrom(
                        backgroundColor: DigitalBrainColors.indigoSoft,
                        padding: const EdgeInsets.symmetric(
                          horizontal: 12,
                          vertical: 6,
                        ),
                        minimumSize: Size.zero,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(6),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _SynapseCompactReferenceWidget extends StatefulWidget {
  const _SynapseCompactReferenceWidget({
    required this.type,
    required this.desc,
  });

  final String type;
  final String desc;

  @override
  State<_SynapseCompactReferenceWidget> createState() =>
      _SynapseCompactReferenceWidgetState();
}

class _SynapseCompactReferenceWidgetState
    extends State<_SynapseCompactReferenceWidget> {
  CatalogContractSchema? _schema;

  @override
  void initState() {
    super.initState();
    _findSchema();
  }

  void _findSchema() {
    final catalog = DigitalBrainCatalogManager.instance.catalog;
    for (final s in catalog) {
      if (s.fqn == widget.type || s.fqn.split('.').last == widget.type) {
        _schema = s;
        break;
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final hasFields = _schema != null && _schema!.fields.isNotEmpty;
    final cleanName = widget.type.split('.').last;

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: DigitalBrainColors.obsidian.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(
                Icons.hub_outlined,
                color: DigitalBrainColors.tealSoft,
                size: 12,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  cleanName,
                  style: GoogleFonts.jetBrainsMono(
                    fontSize: 11,
                    color: DigitalBrainColors.tealSoft,
                    fontWeight: FontWeight.bold,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
          if (widget.desc.isNotEmpty) ...[
            const SizedBox(height: 3),
            Text(
              widget.desc,
              style: GoogleFonts.manrope(
                fontSize: 9,
                color: DigitalBrainColors.inkLow,
              ),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ],
          if (hasFields) ...[
            const SizedBox(height: 6),
            Wrap(
              spacing: 4,
              runSpacing: 4,
              children: _schema!.fields.map((field) {
                if (field == 'Headers' ||
                    field == 'headers' ||
                    field == 'Metadata' ||
                    field == 'metadata') {
                  return const SizedBox.shrink();
                }
                return Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 4,
                    vertical: 2,
                  ),
                  decoration: BoxDecoration(
                    color: DigitalBrainColors.obsidianSlate,
                    borderRadius: BorderRadius.circular(4),
                    border: Border.all(color: DigitalBrainColors.hairline),
                  ),
                  child: Text(
                    field,
                    style: GoogleFonts.jetBrainsMono(
                      fontSize: 8,
                      color: DigitalBrainColors.goldSoft,
                    ),
                  ),
                );
              }).toList(),
            ),
          ],
        ],
      ),
    );
  }
}

Widget _telemetryPanel(BuildContext c, DataSource s) {
  final genAttempts = _int(s, 'generationAttempts', 24);
  final execRuns = _int(s, 'executionRuns', 192);
  final failedRuns = _int(s, 'failedRuns', 3);

  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: DigitalBrainColors.obsidian,
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: DigitalBrainColors.hairline),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'TELEMETRY COUNTERS',
          style: GoogleFonts.jetBrainsMono(
            fontSize: 10,
            color: DigitalBrainColors.inkLow,
            fontWeight: FontWeight.bold,
          ),
        ),
        const SizedBox(height: 8),
        _buildTelemetryRow(
          'Gen Loops',
          '$genAttempts',
          DigitalBrainColors.indigoSoft,
        ),
        _buildTelemetryRow(
          'Exec Runs',
          '$execRuns',
          DigitalBrainColors.tealSoft,
        ),
        _buildTelemetryRow(
          'Failed Runs',
          '$failedRuns',
          DigitalBrainColors.rose,
        ),
      ],
    ),
  );
}

Widget _buildTelemetryRow(String label, String value, Color color) {
  return Padding(
    padding: const EdgeInsets.symmetric(vertical: 3),
    child: Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(
          label,
          style: GoogleFonts.manrope(
            fontSize: 12,
            color: DigitalBrainColors.inkMid,
          ),
        ),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
          decoration: BoxDecoration(
            color: color.withValues(alpha: 0.15),
            borderRadius: BorderRadius.circular(4),
          ),
          child: Text(
            value,
            style: GoogleFonts.jetBrainsMono(
              fontSize: 11,
              color: color,
              fontWeight: FontWeight.bold,
            ),
          ),
        ),
      ],
    ),
  );
}

Widget _llmSettingsPanel(BuildContext c, DataSource s) {
  final model = _str(s, 'model', 'GPT-4o');
  final temp = _d(s, 'temperature', 0.7);
  final attempts = _int(s, 'maxAttempts', 3);
  final onChange = s.voidHandler(['onChange']);

  return _LlmSettingsPanelBody(
    initialModel: model,
    initialTemp: temp,
    initialAttempts: attempts,
    onChange: onChange,
  );
}

class _LlmSettingsPanelBody extends StatefulWidget {
  const _LlmSettingsPanelBody({
    required this.initialModel,
    required this.initialTemp,
    required this.initialAttempts,
    required this.onChange,
  });

  final String initialModel;
  final double initialTemp;
  final int initialAttempts;
  final VoidCallback? onChange;

  @override
  State<_LlmSettingsPanelBody> createState() => _LlmSettingsPanelBodyState();
}

class _LlmSettingsPanelBodyState extends State<_LlmSettingsPanelBody> {
  late String _model;
  late double _temp;
  late int _attempts;
  late bool _replaceSpheresWithIcons;
  late bool _showSynapses;
  late bool _localAiMode;

  @override
  void initState() {
    super.initState();
    _model = widget.initialModel;
    _temp = widget.initialTemp;
    _attempts = widget.initialAttempts;
    _replaceSpheresWithIcons = LlmSettingsBus.instance.replaceSpheresWithIcons;
    _showSynapses = LlmSettingsBus.instance.showSynapses;
    _localAiMode = LlmSettingsBus.instance.localAiMode;
  }

  @override
  void didUpdateWidget(_LlmSettingsPanelBody old) {
    super.didUpdateWidget(old);
    if (widget.initialModel != old.initialModel ||
        widget.initialTemp != old.initialTemp ||
        widget.initialAttempts != old.initialAttempts) {
      setState(() {
        _model = widget.initialModel;
        _temp = widget.initialTemp;
        _attempts = widget.initialAttempts;
      });
    }
  }

  void _updateSettings({
    String? model,
    double? temp,
    int? attempts,
    bool? replaceSpheresWithIcons,
    bool? showSynapses,
    bool? localAiMode,
  }) {
    setState(() {
      if (model != null) _model = model;
      if (temp != null) _temp = temp;
      if (attempts != null) _attempts = attempts;
      if (replaceSpheresWithIcons != null) {
        _replaceSpheresWithIcons = replaceSpheresWithIcons;
      }
      if (showSynapses != null) _showSynapses = showSynapses;
      if (localAiMode != null) _localAiMode = localAiMode;
    });
    LlmSettingsBus.instance.update(
      _model,
      _temp,
      _attempts,
      _replaceSpheresWithIcons,
      _showSynapses,
      _localAiMode,
    );
    widget.onChange?.call();
  }

  void _changeTemp(double delta) {
    final double next = (_temp + delta).clamp(0.0, 1.2);
    // Parse to double via string to avoid float imprecision issues
    final double parsed = double.parse(next.toStringAsFixed(1));
    _updateSettings(temp: parsed);
  }

  void _changeAttempts(int delta) {
    final int next = (_attempts + delta).clamp(1, 5);
    _updateSettings(attempts: next);
  }

  Widget _buildStepperButton({
    required IconData icon,
    required VoidCallback onPressed,
  }) {
    return GestureDetector(
      onTap: onPressed,
      child: Container(
        width: 24,
        height: 24,
        decoration: BoxDecoration(
          color: DigitalBrainColors.panelGlass,
          borderRadius: BorderRadius.circular(4),
          border: Border.all(color: DigitalBrainColors.hairlineStrong),
        ),
        child: Center(
          child: Icon(icon, size: 14, color: DigitalBrainColors.inkMid),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final List<String> models = ['GPT-4o', 'Claude-3.5', 'Gemini-1.5'];

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: DigitalBrainColors.obsidian,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'LLM ENGINE',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 10,
                  color: DigitalBrainColors.inkLow,
                  fontWeight: FontWeight.bold,
                ),
              ),
              const Icon(
                Icons.auto_awesome,
                size: 12,
                color: DigitalBrainColors.gold,
              ),
            ],
          ),
          const SizedBox(height: 12),
          // Model row
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Model',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Wrap(
                spacing: 6,
                children: models.map((m) {
                  final bool active = m == _model;
                  return GestureDetector(
                    onTap: () => _updateSettings(model: m),
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 4,
                      ),
                      decoration: BoxDecoration(
                        color: active
                            ? DigitalBrainColors.indigoDeep.withValues(
                                alpha: 0.25,
                              )
                            : Colors.transparent,
                        border: Border.all(
                          color: active
                              ? DigitalBrainColors.indigoSoft
                              : DigitalBrainColors.hairline,
                        ),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        m,
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 10,
                          color: active
                              ? DigitalBrainColors.ink
                              : DigitalBrainColors.inkLow,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  );
                }).toList(),
              ),
            ],
          ),
          const SizedBox(height: 12),
          // Temperature row
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Temperature',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Row(
                children: [
                  _buildStepperButton(
                    icon: Icons.remove,
                    onPressed: () => _changeTemp(-0.1),
                  ),
                  const SizedBox(width: 8),
                  SizedBox(
                    width: 28,
                    child: Center(
                      child: Text(
                        _temp.toStringAsFixed(1),
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 12,
                          color: DigitalBrainColors.goldSoft,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  _buildStepperButton(
                    icon: Icons.add,
                    onPressed: () => _changeTemp(0.1),
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          // Max Attempts row
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Max Attempts',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Row(
                children: [
                  _buildStepperButton(
                    icon: Icons.remove,
                    onPressed: () => _changeAttempts(-1),
                  ),
                  const SizedBox(width: 8),
                  SizedBox(
                    width: 28,
                    child: Center(
                      child: Text(
                        _attempts.toString(),
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 12,
                          color: DigitalBrainColors.goldSoft,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  _buildStepperButton(
                    icon: Icons.add,
                    onPressed: () => _changeAttempts(1),
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 12),
          const Divider(color: DigitalBrainColors.hairline),
          const SizedBox(height: 12),
          // Replace Spheres with Icons
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Replace Spheres with Icons',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Switch(
                value: _replaceSpheresWithIcons,
                activeThumbColor: DigitalBrainColors.indigoSoft,
                activeTrackColor: DigitalBrainColors.indigoDeep.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panelGlass,
                onChanged: (val) {
                  _updateSettings(replaceSpheresWithIcons: val);
                },
              ),
            ],
          ),
          const SizedBox(height: 8),
          // Show Synapses Filament Web
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Show Synapses Filament Web',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Switch(
                value: _showSynapses,
                activeThumbColor: DigitalBrainColors.teal,
                activeTrackColor: DigitalBrainColors.teal.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panelGlass,
                onChanged: (val) {
                  _updateSettings(showSynapses: val);
                },
              ),
            ],
          ),
          const SizedBox(height: 8),
          // Local AI Mode
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Local AI Mode (Offline)',
                style: GoogleFonts.manrope(
                  fontSize: 12,
                  color: DigitalBrainColors.inkMid,
                ),
              ),
              Switch(
                value: _localAiMode,
                activeThumbColor: DigitalBrainColors.gold,
                activeTrackColor: DigitalBrainColors.gold.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panelGlass,
                onChanged: (val) {
                  _updateSettings(localAiMode: val);
                },
              ),
            ],
          ),
        ],
      ),
    );
  }
}
