part of 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

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
