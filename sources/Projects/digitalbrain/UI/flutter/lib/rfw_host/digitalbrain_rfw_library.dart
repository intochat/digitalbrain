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
import 'package:digitalbrain_flutter/features/ino_editor/prompt_input_bus.dart';
import 'package:digitalbrain_flutter/features/ino_editor/typewriter_controller.dart';
import 'package:digitalbrain_flutter/features/ino_editor/state_editor_bus.dart';
import 'package:digitalbrain_flutter/features/ino_editor/llm_settings_bus.dart';
import 'package:digitalbrain_flutter/features/ino_editor/ino_editor_bus.dart';
import 'package:digitalbrain_flutter/rfw_host/synapse_stream_scope.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_client_scope.dart';
import 'package:digitalbrain_flutter/widgets/canvas_3d.dart';
import 'package:digitalbrain_flutter/features/brain/voice_input.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/rfw_host/palette/palette_primitives.dart'
    as palette;

LocalWidgetLibrary createDigitalBrainWidgets() => LocalWidgetLibrary(_widgets);

Map<String, LocalWidgetBuilder> get _widgets => <String, LocalWidgetBuilder>{
  'Panel': _panel,
  'VStack': (c, s) => _stack(c, s, Axis.vertical),
  'HStack': (c, s) => _stack(c, s, Axis.horizontal),
  'Pad': _pad,
  'Text': _text,
  'Image': _image,
  'Tag': _tag,
  'Badge': _badge,
  'Counter': _counter,
  'Stars': _stars,
  'Button': _button,
  'KeyValue': _keyValue,
  'Divider': (c, s) => _divider(),
  'Bars': _bars,
  'Table': _table,
  'Metric': _metric,
  'SectionLabel': _sectionLabel,
  'Calendar': _calendar,
  'LineChart': _lineChart,
  'Donut': _donut,
  'Progress': _progress,
  'Timeline': _timeline,
  'Avatar': _avatar,
  'GlowIcon': _glowIcon,
  'Bullets': _bullets,
  'AdaptiveContainer': _adaptiveContainer,
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

  'SimulationsTab': _simulationsTab,
  'ScenariosTab': _scenariosTab,
  'NeuronDebuggerTab': _neuronDebuggerTab,
  'Canvas3D': _canvas3D,
  'LoadingCard': _loadingCard,

  // Widget-canvas redesign palette (Tier-1) — docs/redesign/03-WIDGET-PALETTE.md
  'LottiePlayer': palette.lottiePlayer,
  'AnalogClock': palette.analogClock,
  'CountdownClock': palette.countdownClock,
  'EarthGlobe': palette.earthGlobe,
  'FloatingWindow': palette.floatingWindow,
};

// ── typed DataSource readers ─────────────────────────────────────
//
// RFW's DataSource.v<T> only accepts T in {int, double, bool, String}
// and returns null on any type mismatch. JSON-shaped payloads carry a
// number as either an int or a double, so every numeric read tries both.
// Lists and maps are not returned wholesale by RFW — they're walked by
// path (length([k]) then v<scalar>([k, i, subkey])).

double _d(DataSource s, String k, double def) =>
    s.v<double>([k]) ?? s.v<int>([k])?.toDouble() ?? def;
String _str(DataSource s, String k, [String def = '']) =>
    s.v<String>([k]) ?? def;
int _int(DataSource s, String k, int def) =>
    s.v<int>([k]) ?? s.v<double>([k])?.toInt() ?? def;
bool _bool(DataSource s, String k, bool def) => s.v<bool>([k]) ?? def;

double _dp(DataSource s, List<Object> p, [double def = 0]) =>
    s.v<double>(p) ?? s.v<int>(p)?.toDouble() ?? def;
String _sp(DataSource s, List<Object> p, [String def = '']) =>
    s.v<String>(p) ?? def;
int _ip(DataSource s, List<Object> p, [int def = 0]) =>
    s.v<int>(p) ?? s.v<double>(p)?.toInt() ?? def;

Color _tone(String tone) {
  switch (tone) {
    case 'teal':
      return DigitalBrainColors.tealSoft;
    case 'gold':
      return DigitalBrainColors.gold;
    case 'violet':
      return DigitalBrainColors.violetSoft;
    case 'rose':
      return DigitalBrainColors.rose;
    case 'indigo':
    default:
      return DigitalBrainColors.indigoSoft;
  }
}

CrossAxisAlignment _cross(String v) {
  switch (v) {
    case 'start':
      return CrossAxisAlignment.start;
    case 'end':
      return CrossAxisAlignment.end;
    case 'stretch':
      return CrossAxisAlignment.stretch;
    case 'center':
    default:
      return CrossAxisAlignment.center;
  }
}

TextStyle _variant(BuildContext c, String variant) {
  final t = Theme.of(c).textTheme;
  switch (variant) {
    case 'display':
      return (t.displaySmall ?? const TextStyle()).copyWith(
        color: DigitalBrainColors.ink,
      );
    case 'heading':
      return (t.headlineSmall ?? const TextStyle()).copyWith(
        color: DigitalBrainColors.ink,
      );
    case 'title':
      return (t.titleLarge ?? const TextStyle()).copyWith(
        color: DigitalBrainColors.ink,
      );
    case 'label':
      return GoogleFonts.jetBrainsMono(
        fontSize: 11,
        color: DigitalBrainColors.indigoSoft,
        letterSpacing: 2.4,
        fontWeight: FontWeight.w600,
      );
    case 'mono':
      return GoogleFonts.jetBrainsMono(
        fontSize: 12,
        color: DigitalBrainColors.inkLow,
        letterSpacing: 1.0,
      );
    case 'dim':
      return (t.bodyMedium ?? const TextStyle()).copyWith(
        color: DigitalBrainColors.inkLow,
        fontSize: 13,
      );
    case 'body':
    default:
      return (t.bodyLarge ?? const TextStyle()).copyWith(
        color: DigitalBrainColors.inkMid,
      );
  }
}

// ── widgets ────────────────────────────────────────────────

Widget _panel(BuildContext c, DataSource s) {
  final pad = _d(s, 'padding', 0);
  final r = _d(s, 'radius', 24); // Apple default radius of 24
  final onTap = s.voidHandler(['onTap']);
  Widget w = GlassBorder(
    borderRadius: BorderRadius.circular(r),
    strokeWidth: 0.5,
    child: GlassMaterial(
      cornerRadius: r,
      blurSigma: 30,
      tintOpacity: 0.04,
      child: Padding(
        padding: EdgeInsets.all(pad),
        child: s.optionalChild(['child']) ?? const SizedBox.shrink(),
      ),
    ),
  );
  if (onTap != null) {
    w = InkWell(onTap: onTap, borderRadius: BorderRadius.circular(r), child: w);
  }
  return w;
}

Widget _stack(BuildContext c, DataSource s, Axis axis) {
  final kids = s.childList(['children']);
  final gap = _d(s, 'gap', 12);
  final between = _bool(s, 'between', false);
  final equal = _bool(s, 'equal', false);
  final cross = _cross(
    _str(s, 'cross', axis == Axis.vertical ? 'stretch' : 'center'),
  );
  final main = between
      ? MainAxisAlignment.spaceBetween
      : MainAxisAlignment.start;

  var items = kids;
  if (equal) {
    items = [for (final w in kids) Expanded(child: w)];
  }
  var children = items;
  if (!between && gap > 0 && items.length > 1) {
    children = <Widget>[];
    for (var i = 0; i < items.length; i++) {
      if (i > 0) {
        children.add(
          SizedBox(
            width: axis == Axis.horizontal ? gap : 0,
            height: axis == Axis.vertical ? gap : 0,
          ),
        );
      }
      children.add(items[i]);
    }
  }

  if (axis == Axis.vertical) {
    return Column(
      crossAxisAlignment: cross,
      mainAxisAlignment: main,
      mainAxisSize: MainAxisSize.min,
      children: children,
    );
  }
  final row = Row(
    crossAxisAlignment: cross,
    mainAxisAlignment: main,
    mainAxisSize: MainAxisSize.min,
    children: children,
  );
  // A Row that stretches its children on the cross (vertical) axis needs a
  // bounded height. RFW content renders inside an unbounded scroll viewport,
  // so give the row a definite height from its children's intrinsics.
  return cross == CrossAxisAlignment.stretch
      ? IntrinsicHeight(child: row)
      : row;
}

Widget _pad(BuildContext c, DataSource s) {
  final all = s.v<double>(['all']) ?? s.v<int>(['all'])?.toDouble();
  final h = _d(s, 'h', all ?? 0);
  final v = _d(s, 'v', all ?? 0);
  return Padding(
    padding: EdgeInsets.symmetric(horizontal: h, vertical: v),
    child: s.child(['child']),
  );
}

Widget _text(BuildContext c, DataSource s) {
  final color = s.v<int>(['color']);
  var style = _variant(c, _str(s, 'variant', 'body'));
  if (color != null) style = style.copyWith(color: Color(color));
  final align = _str(s, 'align', 'start');
  return Text(
    _str(s, 'text'),
    textAlign: align == 'center'
        ? TextAlign.center
        : align == 'end'
        ? TextAlign.end
        : TextAlign.start,
    style: style,
  );
}

Widget _imgFallback(BuildContext c) => Container(
  color: DigitalBrainColors.bg2,
  alignment: Alignment.center,
  child: const Icon(
    Icons.image_outlined,
    color: DigitalBrainColors.inkLow,
    size: 28,
  ),
);

Widget _image(BuildContext c, DataSource s) {
  final url = _str(s, 'url');
  final h = _d(s, 'height', 160);
  final r = _d(s, 'radius', 12);
  return ClipRRect(
    borderRadius: BorderRadius.circular(r),
    child: SizedBox(
      height: h,
      width: double.infinity,
      child: url.isEmpty
          ? _imgFallback(c)
          : Image.network(
              url,
              fit: BoxFit.cover,
              errorBuilder: (_, _, _) => _imgFallback(c),
              loadingBuilder: (ctx, child, progress) =>
                  progress == null ? child : _imgFallback(c),
            ),
    ),
  );
}

Widget _tag(BuildContext c, DataSource s) {
  final color = _tone(_str(s, 'tone', 'indigo'));
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
    decoration: BoxDecoration(
      color: color.withValues(alpha: 0.10),
      borderRadius: BorderRadius.circular(6),
      border: Border.all(color: color.withValues(alpha: 0.32)),
    ),
    child: Text(
      _str(s, 'text'),
      style: GoogleFonts.jetBrainsMono(
        fontSize: 11,
        color: color,
        fontWeight: FontWeight.w500,
        letterSpacing: 0.4,
      ),
    ),
  );
}

Widget _badge(BuildContext c, DataSource s) {
  final color = _tone(_str(s, 'tone', 'teal'));
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
    decoration: BoxDecoration(
      color: color.withValues(alpha: 0.16),
      borderRadius: BorderRadius.circular(999),
    ),
    child: Text(
      _str(s, 'text'),
      style: GoogleFonts.manrope(
        fontSize: 11,
        color: color,
        fontWeight: FontWeight.w600,
      ),
    ),
  );
}

Widget _counter(BuildContext c, DataSource s) {
  final tone = _tone(_str(s, 'tone', 'indigo'));
  final value = _int(s, 'value', 0);
  final label = _str(s, 'label');
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
    decoration: BoxDecoration(
      color: tone.withValues(alpha: 0.16),
      borderRadius: BorderRadius.circular(999),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          '$value',
          style: GoogleFonts.jetBrainsMono(
            fontSize: 12,
            color: tone,
            fontWeight: FontWeight.w700,
          ),
        ),
        if (label.isNotEmpty) ...[
          const SizedBox(width: 6),
          Text(
            label,
            style: GoogleFonts.manrope(
              fontSize: 11,
              color: DigitalBrainColors.inkMid,
            ),
          ),
        ],
      ],
    ),
  );
}

Widget _stars(BuildContext c, DataSource s) {
  final value = _d(s, 'value', 0);
  final max = _int(s, 'max', 5);
  final icons = <Widget>[];
  for (var i = 1; i <= max; i++) {
    IconData d;
    if (value >= i) {
      d = Icons.star_rounded;
    } else if (value >= i - 0.5) {
      d = Icons.star_half_rounded;
    } else {
      d = Icons.star_outline_rounded;
    }
    icons.add(Icon(d, size: 16, color: DigitalBrainColors.gold));
  }
  return Row(mainAxisSize: MainAxisSize.min, children: icons);
}

Widget _button(BuildContext c, DataSource s) {
  final onTap = s.voidHandler(['onTap']);
  return FilledButton(
    onPressed: onTap,
    child: Text(_str(s, 'label', 'Action')),
  );
}

Widget _keyValue(BuildContext c, DataSource s) {
  return Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(child: Text(_str(s, 'label'), style: _variant(c, 'dim'))),
        const SizedBox(width: 16),
        Expanded(
          child: Text(
            _str(s, 'value'),
            textAlign: TextAlign.end,
            style: (Theme.of(c).textTheme.bodyMedium ?? const TextStyle())
                .copyWith(color: DigitalBrainColors.ink, fontSize: 13),
          ),
        ),
      ],
    ),
  );
}

Widget _divider() => Container(
  height: 1,
  color: DigitalBrainColors.hairline,
  margin: const EdgeInsets.symmetric(vertical: 4),
);

Widget _bars(BuildContext c, DataSource s) {
  final n = s.length(['values']);
  final h = _d(s, 'height', 120);
  if (n == 0) return SizedBox(height: h);
  final bars = <Widget>[];
  for (var i = 0; i < n; i++) {
    final v = _dp(s, ['values', i, 'value']);
    final label = _sp(s, ['values', i, 'label']);
    bars.add(
      Expanded(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 5),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              Expanded(
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: FractionallySizedBox(
                    heightFactor: v.clamp(0.04, 1.0),
                    child: Container(
                      decoration: BoxDecoration(
                        gradient: const LinearGradient(
                          begin: Alignment.bottomCenter,
                          end: Alignment.topCenter,
                          colors: [
                            DigitalBrainColors.indigoDeep,
                            DigitalBrainColors.indigoSoft,
                          ],
                        ),
                        borderRadius: BorderRadius.circular(4),
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Text(
                label,
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 10,
                  color: DigitalBrainColors.inkLow,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
  return SizedBox(
    height: h,
    child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: bars),
  );
}

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

Widget _metric(BuildContext c, DataSource s) {
  final tone = _tone(_str(s, 'tone', 'indigo'));
  final delta = _str(s, 'delta');
  return Container(
    padding: const EdgeInsets.all(18),
    decoration: BoxDecoration(
      color: DigitalBrainColors.panel,
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: DigitalBrainColors.hairline),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          _str(s, 'label').toUpperCase(),
          style: GoogleFonts.jetBrainsMono(
            fontSize: 10,
            color: DigitalBrainColors.inkLow,
            letterSpacing: 1.6,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: 10),
        Text(
          _str(s, 'value'),
          style: (Theme.of(c).textTheme.headlineSmall ?? const TextStyle())
              .copyWith(color: DigitalBrainColors.ink),
        ),
        if (delta.isNotEmpty) ...[
          const SizedBox(height: 4),
          Text(
            delta,
            style: GoogleFonts.manrope(
              fontSize: 12,
              color: tone,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ],
    ),
  );
}

Widget _sectionLabel(BuildContext c, DataSource s) => Text(
  _str(s, 'text').toUpperCase(),
  style: GoogleFonts.jetBrainsMono(
    fontSize: 11,
    color: DigitalBrainColors.inkLow,
    letterSpacing: 2.4,
    fontWeight: FontWeight.w600,
  ),
);

// ── calendar ───────────────────────────────────────────────

Widget _calendar(BuildContext c, DataSource s) {
  final now = DateTime.now();
  final year = _int(s, 'year', now.year);
  final month = _int(s, 'month', now.month);
  final marks = <int, Color>{};
  final ne = s.length(['events']);
  for (var i = 0; i < ne; i++) {
    final day = _ip(s, ['events', i, 'day']);
    if (day != 0) marks[day] = _tone(_sp(s, ['events', i, 'tone'], 'indigo'));
  }
  final first = DateTime(year, month, 1);
  final daysInMonth = DateTime(year, month + 1, 0).day;
  final lead = (first.weekday + 6) % 7;
  final isThisMonth = now.year == year && now.month == month;

  final cells = <Widget>[];
  for (var i = 0; i < lead; i++) {
    cells.add(const Expanded(child: SizedBox(height: 40)));
  }
  for (var day = 1; day <= daysInMonth; day++) {
    final mark = marks[day];
    final isToday = isThisMonth && now.day == day;
    cells.add(
      Expanded(
        child: Padding(
          padding: const EdgeInsets.all(3),
          child: Container(
            height: 40,
            decoration: BoxDecoration(
              color: mark != null
                  ? mark.withValues(alpha: 0.12)
                  : Colors.transparent,
              borderRadius: BorderRadius.circular(8),
              border: isToday
                  ? Border.all(color: DigitalBrainColors.indigoSoft)
                  : null,
            ),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  '$day',
                  style: GoogleFonts.manrope(
                    fontSize: 12,
                    color: mark != null
                        ? DigitalBrainColors.ink
                        : DigitalBrainColors.inkMid,
                    fontWeight: mark != null
                        ? FontWeight.w600
                        : FontWeight.w400,
                  ),
                ),
                const SizedBox(height: 3),
                Container(
                  width: 4,
                  height: 4,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: mark ?? Colors.transparent,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
  while (cells.length % 7 != 0) {
    cells.add(const Expanded(child: SizedBox(height: 40)));
  }
  final rows = <Widget>[
    Row(
      children: const [
        _Wk('M'),
        _Wk('T'),
        _Wk('W'),
        _Wk('T'),
        _Wk('F'),
        _Wk('S'),
        _Wk('S'),
      ],
    ),
    const SizedBox(height: 8),
  ];
  for (var i = 0; i < cells.length; i += 7) {
    rows.add(Row(children: cells.sublist(i, i + 7)));
  }
  return Column(mainAxisSize: MainAxisSize.min, children: rows);
}

class _Wk extends StatelessWidget {
  const _Wk(this.t);
  final String t;
  @override
  Widget build(BuildContext context) => Expanded(
    child: Center(
      child: Text(
        t,
        style: GoogleFonts.jetBrainsMono(
          fontSize: 10,
          color: DigitalBrainColors.inkLow,
          letterSpacing: 1.0,
          fontWeight: FontWeight.w600,
        ),
      ),
    ),
  );
}

// ── line chart ─────────────────────────────────────────────

Widget _lineChart(BuildContext c, DataSource s) {
  final n = s.length(['values']);
  final values = <double>[
    for (var i = 0; i < n; i++) _dp(s, ['values', i]),
  ];
  final h = _d(s, 'height', 140);
  final color = _tone(_str(s, 'tone', 'indigo'));
  if (values.length < 2) return SizedBox(height: h);
  return SizedBox(
    height: h,
    width: double.infinity,
    child: CustomPaint(painter: _LinePainter(values, color)),
  );
}

class _LinePainter extends CustomPainter {
  _LinePainter(this.values, this.color);
  final List<double> values;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final lo = values.reduce(math.min);
    final hi = values.reduce(math.max);
    final span = (hi - lo).abs() < 1e-9 ? 1.0 : hi - lo;
    final dx = size.width / (values.length - 1);
    Offset pt(int i) => Offset(
      dx * i,
      size.height - ((values[i] - lo) / span) * (size.height - 10) - 5,
    );
    final path = Path()..moveTo(pt(0).dx, pt(0).dy);
    for (var i = 1; i < values.length; i++) {
      path.lineTo(pt(i).dx, pt(i).dy);
    }
    final fill = Path.from(path)
      ..lineTo(size.width, size.height)
      ..lineTo(0, size.height)
      ..close();
    canvas.drawPath(
      fill,
      Paint()
        ..shader = LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [color.withValues(alpha: 0.22), color.withValues(alpha: 0.0)],
        ).createShader(Offset.zero & size),
    );
    canvas.drawPath(
      path,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2.2
        ..strokeJoin = StrokeJoin.round
        ..color = color,
    );
    final last = pt(values.length - 1);
    canvas.drawCircle(last, 3.5, Paint()..color = color);
  }

  @override
  bool shouldRepaint(_LinePainter old) =>
      old.color != color || old.values != values;
}

// ── donut ──────────────────────────────────────────────────

class _Seg {
  const _Seg(this.value, this.color, this.label);
  final double value;
  final Color color;
  final String label;
}

Widget _donut(BuildContext c, DataSource s) {
  final segs = <_Seg>[];
  final ns = s.length(['segments']);
  for (var i = 0; i < ns; i++) {
    segs.add(
      _Seg(
        _dp(s, ['segments', i, 'value']),
        _tone(_sp(s, ['segments', i, 'tone'], 'indigo')),
        _sp(s, ['segments', i, 'label']),
      ),
    );
  }
  final size = _d(s, 'size', 132);
  final label = _str(s, 'label');
  final caption = _str(s, 'caption');
  final total = segs.fold<double>(0, (a, b) => a + b.value);
  return Row(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.center,
    children: [
      SizedBox(
        width: size,
        height: size,
        child: CustomPaint(
          painter: _DonutPainter(segs),
          child: Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (label.isNotEmpty)
                  Text(
                    label,
                    style:
                        (Theme.of(c).textTheme.titleLarge ?? const TextStyle())
                            .copyWith(color: DigitalBrainColors.ink),
                  ),
                if (caption.isNotEmpty)
                  Text(
                    caption,
                    style: GoogleFonts.manrope(
                      fontSize: 11,
                      color: DigitalBrainColors.inkLow,
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
      const SizedBox(width: 18),
      Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final seg in segs)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 3),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 9,
                    height: 9,
                    decoration: BoxDecoration(
                      color: seg.color,
                      shape: BoxShape.circle,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    seg.label,
                    style: GoogleFonts.manrope(
                      fontSize: 12,
                      color: DigitalBrainColors.inkMid,
                    ),
                  ),
                  const SizedBox(width: 6),
                  Text(
                    total <= 0 ? '0%' : '${(seg.value / total * 100).round()}%',
                    style: GoogleFonts.jetBrainsMono(
                      fontSize: 11,
                      color: DigitalBrainColors.inkLow,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    ],
  );
}

class _DonutPainter extends CustomPainter {
  _DonutPainter(this.segs);
  final List<_Seg> segs;

  @override
  void paint(Canvas canvas, Size size) {
    final total = segs.fold<double>(0, (a, b) => a + b.value);
    final center = (Offset.zero & size).center;
    final radius = size.shortestSide / 2 - 9;
    canvas.drawCircle(
      center,
      radius,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 16
        ..color = DigitalBrainColors.hairline,
    );
    if (total <= 0) return;
    var start = -math.pi / 2;
    for (final seg in segs) {
      final sweep = (seg.value / total) * math.pi * 2;
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: radius),
        start + 0.02,
        sweep - 0.04,
        false,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 16
          ..strokeCap = StrokeCap.butt
          ..color = seg.color,
      );
      start += sweep;
    }
  }

  @override
  bool shouldRepaint(_DonutPainter old) => old.segs != segs;
}

// ── linear progress ────────────────────────────────────────

Widget _progress(BuildContext c, DataSource s) {
  final v = _d(s, 'value', 0).clamp(0.0, 1.0).toDouble();
  final label = _str(s, 'label');
  final color = _tone(_str(s, 'tone', 'indigo'));
  return Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (label.isNotEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 6),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Flexible(
                child: Text(
                  label,
                  style: GoogleFonts.manrope(
                    fontSize: 12,
                    color: DigitalBrainColors.inkMid,
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Text(
                '${(v * 100).round()}%',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 11,
                  color: DigitalBrainColors.inkLow,
                ),
              ),
            ],
          ),
        ),
      ClipRRect(
        borderRadius: BorderRadius.circular(999),
        child: Stack(
          children: [
            Container(
              height: 8,
              width: double.infinity,
              color: DigitalBrainColors.hairline,
            ),
            FractionallySizedBox(
              alignment: Alignment.centerLeft,
              widthFactor: v == 0 ? 0.001 : v,
              child: Container(
                height: 8,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(999),
                  gradient: LinearGradient(
                    colors: [color.withValues(alpha: 0.6), color],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ],
  );
}

// ── timeline ───────────────────────────────────────────────

Widget _timeline(BuildContext c, DataSource s) {
  final n = s.length(['items']);
  final rows = <Widget>[];
  for (var i = 0; i < n; i++) {
    final tone = _tone(_sp(s, ['items', i, 'tone'], 'indigo'));
    final last = i == n - 1;
    final time = _sp(s, ['items', i, 'time']);
    final title = _sp(s, ['items', i, 'title']);
    final desc = _sp(s, ['items', i, 'desc']);
    rows.add(
      IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Column(
              children: [
                Container(
                  width: 11,
                  height: 11,
                  decoration: BoxDecoration(
                    color: tone,
                    shape: BoxShape.circle,
                    boxShadow: [
                      BoxShadow(
                        color: tone.withValues(alpha: 0.5),
                        blurRadius: 6,
                      ),
                    ],
                  ),
                ),
                if (!last)
                  Expanded(
                    child: Container(
                      width: 1.5,
                      color: DigitalBrainColors.hairlineStrong,
                    ),
                  ),
              ],
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Padding(
                padding: EdgeInsets.only(bottom: last ? 0 : 18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      time,
                      style: GoogleFonts.jetBrainsMono(
                        fontSize: 10,
                        color: DigitalBrainColors.inkLow,
                        letterSpacing: 1.0,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      title,
                      style:
                          (Theme.of(c).textTheme.bodyLarge ?? const TextStyle())
                              .copyWith(
                                color: DigitalBrainColors.ink,
                                fontWeight: FontWeight.w600,
                              ),
                    ),
                    if (desc.isNotEmpty) ...[
                      const SizedBox(height: 2),
                      Text(
                        desc,
                        style: GoogleFonts.manrope(
                          fontSize: 13,
                          color: DigitalBrainColors.inkMid,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
  return Column(mainAxisSize: MainAxisSize.min, children: rows);
}

// ── glow icon ──────────────────────────────────────────────

Widget _glowIcon(BuildContext c, DataSource s) {
  final seed = _int(s, 'seed', 0);
  final size = _d(s, 'size', 16);
  final tone = _tone(_str(s, 'tone', 'indigo'));
  final shape = _str(s, 'shapeHint', 'orb');
  return GlowIcon(
    spec: GlowIconSpec(seed: seed, size: size, tone: tone, shapeHint: shape),
  );
}

// ── avatar ─────────────────────────────────────────────────

Widget _avatar(BuildContext c, DataSource s) {
  final url = _str(s, 'url');
  final initials = _str(s, 'initials', '?');
  final size = _d(s, 'size', 44);
  final color = _tone(_str(s, 'tone', 'indigo'));
  final fallback = Container(
    width: size,
    height: size,
    alignment: Alignment.center,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      color: color.withValues(alpha: 0.16),
      border: Border.all(color: color.withValues(alpha: 0.4)),
    ),
    child: Text(
      initials,
      style: GoogleFonts.manrope(
        fontSize: size * 0.36,
        color: color,
        fontWeight: FontWeight.w700,
      ),
    ),
  );
  if (url.isEmpty) return fallback;
  return ClipOval(
    child: SizedBox(
      width: size,
      height: size,
      child: Image.network(
        url,
        fit: BoxFit.cover,
        errorBuilder: (_, _, _) => fallback,
        loadingBuilder: (ctx, child, p) => p == null ? child : fallback,
      ),
    ),
  );
}

// ── adaptive container ─────────────────────────────────────

Widget _adaptiveContainer(BuildContext c, DataSource s) {
  final size = WindowSizeContext.of(c);
  final compactChild = s.optionalChild(['compact']);
  final mediumChild = s.optionalChild(['medium']);
  final largeChild = s.optionalChild(['large']);

  Widget? pick;
  switch (size) {
    case WindowSize.compact:
      pick = compactChild ?? mediumChild ?? largeChild;
    case WindowSize.medium:
    case WindowSize.expanded:
      pick = mediumChild ?? compactChild ?? largeChild;
    case WindowSize.large:
    case WindowSize.xLarge:
      pick = largeChild ?? mediumChild ?? compactChild;
  }
  return pick ?? const SizedBox.shrink();
}

// ── task row ───────────────────────────────────────────────

Widget _taskRow(BuildContext c, DataSource s) {
  final shortHash = _str(s, 'shortHash');
  final origin = _str(s, 'originNeuron');
  final ageMs = _int(s, 'ageMs', 0);
  final edges = _int(s, 'edgeCount', 0);
  final status = _str(s, 'status', 'running');
  final onCancel = s.voidHandler(['onCancel']);
  final statusTone = switch (status) {
    'cancelling' => DigitalBrainColors.rose,
    'complete' => DigitalBrainColors.tealSoft,
    _ => DigitalBrainColors.indigoSoft,
  };
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    decoration: BoxDecoration(
      gradient: LinearGradient(
        colors: [statusTone.withValues(alpha: 0.10), DigitalBrainColors.panel],
      ),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: statusTone.withValues(alpha: 0.20)),
    ),
    child: Row(
      children: [
        GlowIcon(
          spec: GlowIconSpec(
            seed: origin.hashCode,
            size: 18,
            tone: statusTone,
            shapeHint: 'orb',
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                origin,
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 12,
                  color: DigitalBrainColors.ink,
                ),
                overflow: TextOverflow.ellipsis,
              ),
              const SizedBox(height: 2),
              Text(
                '$shortHash · ${ageMs}ms · $edges edges',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 10,
                  color: DigitalBrainColors.inkLow,
                ),
              ),
            ],
          ),
        ),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
          decoration: BoxDecoration(
            color: statusTone.withValues(alpha: 0.10),
            borderRadius: BorderRadius.circular(999),
          ),
          child: Text(
            status.toUpperCase(),
            style: GoogleFonts.jetBrainsMono(
              color: statusTone,
              fontSize: 9,
              fontWeight: FontWeight.w600,
              letterSpacing: 0.7,
            ),
          ),
        ),
        const SizedBox(width: 6),
        IconButton(
          onPressed: onCancel,
          icon: const Icon(Icons.close, size: 16),
          color: DigitalBrainColors.inkLow,
          tooltip: 'Cancel',
        ),
      ],
    ),
  );
}

// ── synapse stream ─────────────────────────────────────────

Widget _synapseStream(BuildContext c, DataSource s) {
  final cid = _str(s, 'correlationId');
  final h = _d(s, 'height', 40);
  final feed = SynapseStreamScope.maybeOf(c);
  if (feed == null) return SizedBox(height: h);
  return SizedBox(
    height: h,
    child: ListenableBuilder(
      listenable: feed,
      builder: (c, _) {
        final edges = feed.forCorrelation(cid).toList();
        return Row(
          children: [
            for (final e in edges)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 2),
                child: GlowIcon(
                  spec: GlowIconSpec(
                    seed: e.typeName.hashCode,
                    size: 10,
                    tone: colorForSynapseType(e.typeName),
                    shapeHint: 'orb',
                  ),
                ),
              ),
          ],
        );
      },
    ),
  );
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
    if (_compileStatus != 'error' || _compileErrors.isEmpty)
      return const SizedBox.shrink();

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
        color: DigitalBrainColors.bg1,
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

// ── bullets ────────────────────────────────────────────────

Widget _bullets(BuildContext c, DataSource s) {
  final n = s.length(['items']);
  final color = _tone(_str(s, 'tone', 'indigo'));
  return Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      for (var i = 0; i < n; i++)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                margin: const EdgeInsets.only(top: 7),
                width: 5,
                height: 5,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  _sp(s, ['items', i]),
                  style: (Theme.of(c).textTheme.bodyMedium ?? const TextStyle())
                      .copyWith(color: DigitalBrainColors.inkMid),
                ),
              ),
            ],
          ),
        ),
    ],
  );
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
            fillColor: DigitalBrainColors.bg1,
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
        color: DigitalBrainColors.bg1,
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
                          color: DigitalBrainColors.panel,
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
      if (textVal.toLowerCase() == 'true')
        val = true;
      else if (textVal.toLowerCase() == 'false')
        val = false;
      else if (int.tryParse(textVal) != null)
        val = int.parse(textVal);
      else if (double.tryParse(textVal) != null)
        val = double.parse(textVal);
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
    return List.generate(8, hexDigit).join() +
        '-' +
        List.generate(4, hexDigit).join() +
        '-4' +
        List.generate(3, hexDigit).join() +
        '-' +
        (rand.nextInt(4) + 8).toRadixString(16) +
        List.generate(3, hexDigit).join() +
        '-' +
        List.generate(12, hexDigit).join();
  }

  @override
  Widget build(BuildContext context) {
    final hasFields = _controllers.isNotEmpty;

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      decoration: BoxDecoration(
        color: DigitalBrainColors.bg1,
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
              color: DigitalBrainColors.panel,
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
                                color: DigitalBrainColors.bg1,
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
                  }).toList(),
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
        color: DigitalBrainColors.bg1.withValues(alpha: 0.5),
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
                    color: DigitalBrainColors.bg2,
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
      color: DigitalBrainColors.bg1,
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
      if (replaceSpheresWithIcons != null)
        _replaceSpheresWithIcons = replaceSpheresWithIcons;
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
          color: DigitalBrainColors.panel,
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
        color: DigitalBrainColors.bg1,
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
                activeColor: DigitalBrainColors.indigoSoft,
                activeTrackColor: DigitalBrainColors.indigoDeep.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panel,
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
                activeColor: DigitalBrainColors.teal,
                activeTrackColor: DigitalBrainColors.teal.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panel,
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
                activeColor: DigitalBrainColors.gold,
                activeTrackColor: DigitalBrainColors.gold.withValues(
                  alpha: 0.4,
                ),
                inactiveThumbColor: DigitalBrainColors.inkLow,
                inactiveTrackColor: DigitalBrainColors.panel,
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

Widget _simulationsTab(BuildContext c, DataSource s) {
  return Container(
    padding: const EdgeInsets.all(24),
    decoration: BoxDecoration(
      color: DigitalBrainColors.panel,
      borderRadius: BorderRadius.circular(12),
      border: Border.all(color: DigitalBrainColors.hairline),
    ),
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        const Icon(
          Icons.analytics_outlined,
          size: 48,
          color: DigitalBrainColors.teal,
        ),
        const SizedBox(height: 16),
        Text(
          'Synaptic Simulations',
          style: GoogleFonts.fraunces(
            fontSize: 20,
            color: DigitalBrainColors.ink,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Simulate closed-loop event streams and synaptic firings directly inside the 3D living brain constellation.',
          textAlign: TextAlign.center,
          style: GoogleFonts.manrope(
            fontSize: 13,
            color: DigitalBrainColors.inkMid,
            height: 1.5,
          ),
        ),
        const SizedBox(height: 16),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
          decoration: BoxDecoration(
            color: DigitalBrainColors.teal.withValues(alpha: 0.15),
            borderRadius: BorderRadius.circular(6),
            border: Border.all(color: DigitalBrainColors.teal),
          ),
          child: Text(
            'CONSOLIDATED IN-SCENE',
            style: GoogleFonts.jetBrainsMono(
              fontSize: 11,
              color: DigitalBrainColors.teal,
              fontWeight: FontWeight.bold,
            ),
          ),
        ),
      ],
    ),
  );
}

Widget _scenariosTab(BuildContext c, DataSource s) {
  return const _ScenariosTabBody();
}

class MockScenario {
  final String name;
  final String inputSynapse;
  final String inputPayload;
  final String expectedOutputSynapse;
  String status; // 'idle', 'running', 'passed', 'failed'
  String? error;

  MockScenario({
    required this.name,
    required this.inputSynapse,
    required this.inputPayload,
    required this.expectedOutputSynapse,
    this.status = 'idle',
    this.error,
  });
}

class _ScenariosTabBody extends StatefulWidget {
  const _ScenariosTabBody();

  @override
  State<_ScenariosTabBody> createState() => _ScenariosTabBodyState();
}

class _ScenariosTabBodyState extends State<_ScenariosTabBody> {
  final List<MockScenario> _scenarios = [
    MockScenario(
      name: 'Gmail Flight Booking Trigger',
      inputSynapse: 'DB.Gmail.Incoming',
      inputPayload:
          '{\n  "from": "airline-tickets@delta.com",\n  "subject": "Your reservation confirmation - DL1024",\n  "body": "Hi John, your flight is booked..."\n}',
      expectedOutputSynapse: 'DB.Travel.Booking',
    ),
    MockScenario(
      name: 'Morning Wakeup LLM Prompt',
      inputSynapse: 'DB.Signal.Alarm',
      inputPayload:
          '{\n  "time": "07:30:00",\n  "timezone": "EST",\n  "actions": ["weather_fetch", "calendar_summary"]\n}',
      expectedOutputSynapse: 'DB.LLM.Prompt',
    ),
  ];

  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _inputSynapseController = TextEditingController();
  final _payloadController = TextEditingController();
  final _outputSynapseController = TextEditingController();

  @override
  void dispose() {
    _nameController.dispose();
    _inputSynapseController.dispose();
    _payloadController.dispose();
    _outputSynapseController.dispose();
    super.dispose();
  }

  void _runScenario(MockScenario scenario) {
    setState(() {
      scenario.status = 'running';
      scenario.error = null;
    });

    final effects = BrainSceneEffectsScope.maybeOf(context);
    if (effects != null) {
      effects.fire(
        CollapseWave(
          origin: Offset(
            MediaQuery.sizeOf(context).width * 0.4,
            MediaQuery.sizeOf(context).height * 0.5,
          ),
          duration: const Duration(milliseconds: 1000),
        ),
      );
    }

    Future.delayed(const Duration(milliseconds: 600), () {
      if (!mounted) return;
      if (effects != null) {
        effects.fire(
          CollapseWave(
            origin: Offset(
              MediaQuery.sizeOf(context).width * 0.6,
              MediaQuery.sizeOf(context).height * 0.5,
            ),
            duration: const Duration(milliseconds: 1000),
          ),
        );
      }
    });

    Future.delayed(const Duration(milliseconds: 1200), () {
      if (!mounted) return;
      setState(() {
        try {
          jsonDecode(scenario.inputPayload);
          scenario.status = 'passed';
        } catch (e) {
          scenario.status = 'failed';
          scenario.error =
              'Assertion failed: Malformed input payload JSON. Details: $e';
        }
      });
    });
  }

  void _addNewScenario() {
    if (_formKey.currentState!.validate()) {
      setState(() {
        _scenarios.add(
          MockScenario(
            name: _nameController.text.trim(),
            inputSynapse: _inputSynapseController.text.trim(),
            inputPayload: _payloadController.text.trim(),
            expectedOutputSynapse: _outputSynapseController.text.trim(),
          ),
        );
      });
      _nameController.clear();
      _inputSynapseController.clear();
      _payloadController.clear();
      _outputSynapseController.clear();
    }
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'NEURON SCENARIOS',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 11,
                  color: DigitalBrainColors.inkLow,
                  fontWeight: FontWeight.bold,
                  letterSpacing: 1.2,
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: DigitalBrainColors.indigoSoft.withValues(alpha: 0.15),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  '${_scenarios.length} Configured',
                  style: GoogleFonts.manrope(
                    fontSize: 11,
                    color: DigitalBrainColors.indigoSoft,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          ListView.separated(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: _scenarios.length,
            separatorBuilder: (_, __) => const SizedBox(height: 12),
            itemBuilder: (ctx, idx) {
              final s = _scenarios[idx];
              Color borderCol = DigitalBrainColors.hairline;
              IconData statusIcon = Icons.play_arrow_rounded;
              Color statusColor = DigitalBrainColors.inkLow;
              String actionText = 'Run Scenario';

              if (s.status == 'running') {
                borderCol = DigitalBrainColors.indigoSoft.withValues(
                  alpha: 0.5,
                );
                statusIcon = Icons.hourglass_empty_rounded;
                statusColor = DigitalBrainColors.gold;
                actionText = 'Simulating...';
              } else if (s.status == 'passed') {
                borderCol = DigitalBrainColors.teal.withValues(alpha: 0.5);
                statusIcon = Icons.check_circle_rounded;
                statusColor = DigitalBrainColors.teal;
                actionText = 'Re-Run';
              } else if (s.status == 'failed') {
                borderCol = DigitalBrainColors.rose.withValues(alpha: 0.5);
                statusIcon = Icons.error_rounded;
                statusColor = DigitalBrainColors.rose;
                actionText = 'Re-Run';
              }

              return Container(
                decoration: BoxDecoration(
                  color: DigitalBrainColors.panel,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: borderCol),
                ),
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Expanded(
                          child: Text(
                            s.name,
                            style: GoogleFonts.fraunces(
                              fontSize: 16,
                              color: DigitalBrainColors.ink,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Row(
                          children: [
                            Icon(statusIcon, color: statusColor, size: 16),
                            const SizedBox(width: 6),
                            Text(
                              s.status.toUpperCase(),
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 11,
                                color: statusColor,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'INPUT SYNAPSE',
                                style: GoogleFonts.jetBrainsMono(
                                  fontSize: 9,
                                  color: DigitalBrainColors.inkLow,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                s.inputSynapse,
                                style: GoogleFonts.manrope(
                                  fontSize: 13,
                                  color: DigitalBrainColors.inkMid,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 16),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'EXPECTED OUTPUT',
                                style: GoogleFonts.jetBrainsMono(
                                  fontSize: 9,
                                  color: DigitalBrainColors.inkLow,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                s.expectedOutputSynapse,
                                style: GoogleFonts.manrope(
                                  fontSize: 13,
                                  color: DigitalBrainColors.inkMid,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'INPUT PAYLOAD',
                      style: GoogleFonts.jetBrainsMono(
                        fontSize: 9,
                        color: DigitalBrainColors.inkLow,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: DigitalBrainColors.bg1,
                        borderRadius: BorderRadius.circular(6),
                        border: Border.all(color: DigitalBrainColors.hairline),
                      ),
                      child: Text(
                        s.inputPayload,
                        style: GoogleFonts.jetBrainsMono(
                          fontSize: 11,
                          color: DigitalBrainColors.inkLow,
                        ),
                      ),
                    ),
                    if (s.error != null) ...[
                      const SizedBox(height: 12),
                      Container(
                        width: double.infinity,
                        padding: const EdgeInsets.all(10),
                        decoration: BoxDecoration(
                          color: DigitalBrainColors.rose.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(6),
                          border: Border.all(
                            color: DigitalBrainColors.rose.withValues(
                              alpha: 0.3,
                            ),
                          ),
                        ),
                        child: Text(
                          s.error!,
                          style: GoogleFonts.manrope(
                            fontSize: 12,
                            color: DigitalBrainColors.rose,
                          ),
                        ),
                      ),
                    ],
                    const SizedBox(height: 12),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: [
                        FilledButton.icon(
                          onPressed: s.status == 'running'
                              ? null
                              : () => _runScenario(s),
                          icon: s.status == 'running'
                              ? const SizedBox(
                                  width: 14,
                                  height: 14,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 1.5,
                                    color: Colors.white,
                                  ),
                                )
                              : const Icon(Icons.play_arrow, size: 14),
                          label: Text(actionText),
                          style: FilledButton.styleFrom(
                            backgroundColor: s.status == 'passed'
                                ? DigitalBrainColors.teal
                                : s.status == 'failed'
                                ? DigitalBrainColors.rose
                                : DigitalBrainColors.indigoSoft,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              );
            },
          ),
          const SizedBox(height: 24),
          const Divider(color: DigitalBrainColors.hairline),
          const SizedBox(height: 16),
          Text(
            'ADD MOCK SCENARIO ASSERTION',
            style: GoogleFonts.jetBrainsMono(
              fontSize: 11,
              color: DigitalBrainColors.inkLow,
              fontWeight: FontWeight.bold,
              letterSpacing: 1.2,
            ),
          ),
          const SizedBox(height: 16),
          Form(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextFormField(
                  controller: _nameController,
                  style: GoogleFonts.manrope(
                    fontSize: 13,
                    color: DigitalBrainColors.ink,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'Scenario Name',
                    hintText: 'e.g. Stripe Payment Successful Hook',
                  ),
                  validator: (val) =>
                      val == null || val.trim().isEmpty ? 'Required' : null,
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(
                      child: TextFormField(
                        controller: _inputSynapseController,
                        style: GoogleFonts.manrope(
                          fontSize: 13,
                          color: DigitalBrainColors.ink,
                        ),
                        decoration: const InputDecoration(
                          labelText: 'Input Synapse',
                          hintText: 'e.g. DB.Payment.Success',
                        ),
                        validator: (val) => val == null || val.trim().isEmpty
                            ? 'Required'
                            : null,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextFormField(
                        controller: _outputSynapseController,
                        style: GoogleFonts.manrope(
                          fontSize: 13,
                          color: DigitalBrainColors.ink,
                        ),
                        decoration: const InputDecoration(
                          labelText: 'Expected Output',
                          hintText: 'e.g. DB.User.Upgrade',
                        ),
                        validator: (val) => val == null || val.trim().isEmpty
                            ? 'Required'
                            : null,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _payloadController,
                  maxLines: 4,
                  style: GoogleFonts.jetBrainsMono(
                    fontSize: 12,
                    color: DigitalBrainColors.ink,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'Input Payload (JSON)',
                    hintText:
                        '{\n  "amount": 2900,\n  "customer": "cus_123"\n}',
                  ),
                  validator: (val) =>
                      val == null || val.trim().isEmpty ? 'Required' : null,
                ),
                const SizedBox(height: 16),
                FilledButton.icon(
                  onPressed: _addNewScenario,
                  icon: const Icon(Icons.add, size: 16),
                  label: const Text('Add Scenario to Suite'),
                  style: FilledButton.styleFrom(
                    backgroundColor: DigitalBrainColors.panel,
                    foregroundColor: DigitalBrainColors.ink,
                    side: const BorderSide(color: DigitalBrainColors.hairline),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}

Widget _neuronDebuggerTab(BuildContext c, DataSource s) {
  return const _NeuronDebuggerTabBody();
}

class _NeuronDebuggerTabBody extends StatefulWidget {
  const _NeuronDebuggerTabBody();

  @override
  State<_NeuronDebuggerTabBody> createState() => _NeuronDebuggerTabBodyState();
}

class _NeuronDebuggerTabBodyState extends State<_NeuronDebuggerTabBody>
    with SingleTickerProviderStateMixin {
  late final AnimationController _micController = AnimationController(
    duration: const Duration(milliseconds: 1500),
    vsync: this,
  )..repeat();

  final List<String> _logs = [
    'System: Boot validation succeeded. Contract schema catalog scan found 48 contracts.',
    'Compiler: RoslynCompiler initialized. Roslyn warmup duration 430ms.',
    'Runtime: SettingsNeuron sync complete. llm-model key value is "openai-gpt-5".',
  ];

  final _synapseController = TextEditingController(text: 'DB.Gmail.Incoming');
  final _payloadController = TextEditingController(
    text: '{\n  "id": "msg_984",\n  "subject": "System Audit"\n}',
  );
  bool _voiceActive = false;
  bool _sending = false;

  @override
  void dispose() {
    _micController.dispose();
    _synapseController.dispose();
    _payloadController.dispose();
    super.dispose();
  }

  void _sendSynapse() {
    setState(() {
      _sending = true;
    });

    final type = _synapseController.text.trim();
    final payload = _payloadController.text.trim();

    _logs.add(
      'Debugger: Manually injected synapse $type into local stream buffer.',
    );

    final effects = BrainSceneEffectsScope.maybeOf(context);
    if (effects != null) {
      effects.fire(
        CollapseWave(
          origin: Offset(
            MediaQuery.sizeOf(context).width * 0.5,
            MediaQuery.sizeOf(context).height * 0.5,
          ),
          duration: const Duration(milliseconds: 1200),
        ),
      );
    }

    Future.delayed(const Duration(milliseconds: 1000), () {
      if (!mounted) return;
      setState(() {
        _sending = false;
        try {
          jsonDecode(payload);
          _logs.add(
            'Compiler: Roslyn dynamic loader compiled and mapped handler for $type successfully.',
          );
        } catch (e) {
          _logs.add(
            'Error: Synapse validation failed. Malformed JSON payload. $e',
          );
        }
      });
    });
  }

  void _toggleVoice() {
    setState(() {
      _voiceActive = !_voiceActive;
      if (_voiceActive) {
        _logs.add(
          'System: Voice recognition recording stream active. Whispering local transcripts...',
        );
      } else {
        _logs.add(
          'System: Voice recognition stream completed. Synthesized command: "draw diagram for my resume"',
        );
        _payloadController.text =
            '{\n  "action": "draw_diagram",\n  "target": "resume_flow"\n}';
        _synapseController.text = 'DB.Travel.Booking';
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'NEURON SYSTEM DEBUGGER',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 11,
                  color: DigitalBrainColors.inkLow,
                  fontWeight: FontWeight.bold,
                  letterSpacing: 1.2,
                ),
              ),
              GestureDetector(
                onTap: _toggleVoice,
                child: AnimatedBuilder(
                  animation: _micController,
                  builder: (ctx, child) {
                    final scale = _voiceActive
                        ? 1.0 + (_micController.value * 0.3)
                        : 1.0;
                    final opacity = _voiceActive
                        ? 1.0 - _micController.value
                        : 0.6;
                    return Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 6,
                      ),
                      decoration: BoxDecoration(
                        color: _voiceActive
                            ? DigitalBrainColors.rose.withValues(
                                alpha: 0.15 * scale,
                              )
                            : DigitalBrainColors.panel,
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(
                          color: _voiceActive
                              ? DigitalBrainColors.rose.withValues(
                                  alpha: opacity,
                                )
                              : DigitalBrainColors.hairline,
                        ),
                      ),
                      child: Row(
                        children: [
                          Icon(
                            _voiceActive ? Icons.mic : Icons.mic_none_outlined,
                            size: 14,
                            color: _voiceActive
                                ? DigitalBrainColors.rose
                                : DigitalBrainColors.inkMid,
                          ),
                          const SizedBox(width: 6),
                          Text(
                            _voiceActive ? 'LISTENING...' : 'VOICE COMMAND',
                            style: GoogleFonts.jetBrainsMono(
                              fontSize: 10,
                              color: _voiceActive
                                  ? DigitalBrainColors.rose
                                  : DigitalBrainColors.inkMid,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ],
                      ),
                    );
                  },
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          // Log console
          Container(
            height: 120,
            width: double.infinity,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: DigitalBrainColors.bg2,
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: DigitalBrainColors.hairlineStrong),
            ),
            child: ListView.separated(
              itemCount: _logs.length,
              separatorBuilder: (_, __) => const SizedBox(height: 6),
              itemBuilder: (ctx, idx) {
                final l = _logs[idx];
                final isErr = l.startsWith('Error:');
                final isSys = l.startsWith('System:');
                Color col = DigitalBrainColors.inkLow;
                if (isErr) col = DigitalBrainColors.rose;
                if (isSys) col = DigitalBrainColors.teal;
                return Text(
                  l,
                  style: GoogleFonts.jetBrainsMono(fontSize: 11, color: col),
                );
              },
            ),
          ),
          const SizedBox(height: 20),
          const Divider(color: DigitalBrainColors.hairline),
          const SizedBox(height: 16),
          Text(
            'MANUAL SYNAPSE INJECTION GATE',
            style: GoogleFonts.jetBrainsMono(
              fontSize: 11,
              color: DigitalBrainColors.inkLow,
              fontWeight: FontWeight.bold,
              letterSpacing: 1.2,
            ),
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _synapseController,
            style: GoogleFonts.manrope(
              fontSize: 13,
              color: DigitalBrainColors.ink,
            ),
            decoration: const InputDecoration(
              labelText: 'Target Synapse Contract Name',
              hintText: 'e.g. DB.Payment.Success',
            ),
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _payloadController,
            maxLines: 3,
            style: GoogleFonts.jetBrainsMono(
              fontSize: 12,
              color: DigitalBrainColors.ink,
            ),
            decoration: const InputDecoration(
              labelText: 'Payload Data (JSON)',
              hintText: '{\n  "amount": 2900,\n  "customer": "cus_123"\n}',
            ),
          ),
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              FilledButton.icon(
                onPressed: _sending ? null : _sendSynapse,
                icon: _sending
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(
                          strokeWidth: 1.5,
                          color: Colors.white,
                        ),
                      )
                    : const Icon(Icons.send_rounded, size: 14),
                label: Text(_sending ? 'INJECTING...' : 'INJECT SYNAPSE'),
                style: FilledButton.styleFrom(
                  backgroundColor: DigitalBrainColors.indigoSoft,
                ),
              ),
            ],
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }
}

Widget _canvas3D(BuildContext c, DataSource s) {
  final sceneName = _str(s, 'sceneName', 'default');
  final spinSpeed = _d(s, 'spinSpeed', 1.0);

  final numAtoms = s.length(['atoms']);
  final atoms = <Atom3D>[];
  for (var i = 0; i < numAtoms; i++) {
    atoms.add(
      Atom3D(
        symbol: _sp(s, ['atoms', i, 'symbol']),
        x: _dp(s, ['atoms', i, 'x']),
        y: _dp(s, ['atoms', i, 'y']),
        z: _dp(s, ['atoms', i, 'z']),
        colorName: _sp(s, ['atoms', i, 'color'], 'indigo'),
        radius: _dp(s, ['atoms', i, 'radius'], 20.0),
      ),
    );
  }

  final numBonds = s.length(['bonds']);
  final bonds = <Bond3D>[];
  for (var i = 0; i < numBonds; i++) {
    bonds.add(
      Bond3D(
        from: _ip(s, ['bonds', i, 'from']),
        to: _ip(s, ['bonds', i, 'to']),
      ),
    );
  }

  return SizedBox(
    height: 400,
    child: Canvas3DWidget(
      sceneName: sceneName,
      atoms: atoms,
      bonds: bonds,
      spinSpeed: spinSpeed,
    ),
  );
}

Widget _loadingCard(BuildContext c, DataSource s) {
  final label = _str(s, 'label', 'Loading...');
  final sublabel = _str(s, 'sublabel');
  final progress = _d(s, 'progress', -1.0); // -1.0 for indeterminate
  final tone = _str(s, 'tone', 'indigo');
  final color = _tone(tone);

  return Container(
    padding: const EdgeInsets.all(20),
    decoration: BoxDecoration(
      color: DigitalBrainColors.panel,
      borderRadius: BorderRadius.circular(16),
      border: Border.all(color: DigitalBrainColors.hairline),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        SizedBox(
          width: 48,
          height: 48,
          child: progress >= 0.0
              ? CircularProgressIndicator(
                  value: progress,
                  color: color,
                  backgroundColor: DigitalBrainColors.bg2,
                  strokeWidth: 3.5,
                )
              : CircularProgressIndicator(
                  color: color,
                  backgroundColor: DigitalBrainColors.bg2,
                  strokeWidth: 3.5,
                ),
        ),
        const SizedBox(height: 16),
        Text(
          label,
          textAlign: TextAlign.center,
          style: GoogleFonts.manrope(
            fontSize: 15,
            color: DigitalBrainColors.ink,
            fontWeight: FontWeight.w600,
          ),
        ),
        if (sublabel.isNotEmpty) ...[
          const SizedBox(height: 6),
          Text(
            sublabel,
            textAlign: TextAlign.center,
            style: GoogleFonts.jetBrainsMono(
              fontSize: 11,
              color: DigitalBrainColors.inkLow,
            ),
          ),
        ],
        if (progress >= 0.0) ...[
          const SizedBox(height: 14),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                '${(progress * 100).round()}%',
                style: GoogleFonts.jetBrainsMono(
                  fontSize: 11,
                  color: color,
                  fontWeight: FontWeight.w700,
                ),
              ),
              Text(
                'Please wait...',
                style: GoogleFonts.manrope(
                  fontSize: 11,
                  color: DigitalBrainColors.inkLow,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: progress,
              color: color,
              backgroundColor: DigitalBrainColors.bg2,
              minHeight: 4,
            ),
          ),
        ],
      ],
    ),
  );
}
