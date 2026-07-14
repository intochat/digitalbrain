part of 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

Widget _panel(BuildContext c, DataSource s) {
  final pad = _d(s, 'padding', 0);
  final r = _d(s, 'radius', 24);
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

Widget _button(BuildContext c, DataSource s) {
  final onTap = s.voidHandler(['onTap']);
  return FilledButton(
    onPressed: onTap,
    child: Text(_str(s, 'label', 'Action')),
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
        colors: [
          statusTone.withValues(alpha: 0.10),
          DigitalBrainColors.panelGlass,
        ],
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
