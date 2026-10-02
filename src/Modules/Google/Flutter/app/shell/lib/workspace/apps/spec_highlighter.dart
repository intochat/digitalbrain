import 'spec_vocabulary.dart';

class SpecSpan {
  const SpecSpan(this.start, this.end, this.kind, this.tokens);
  final int start, end;
  final String kind;
  final List<SpecToken> tokens;
}

List<SpecSpan> highlightSpec(String text, List<SpecToken> vocabulary) {
  final matches = <SpecSpan>[];
  bool word(String c) => RegExp(r'[\p{L}\p{N}_]', unicode: true).hasMatch(c);
  for (final token in vocabulary) {
    if (token.name.isEmpty) continue;
    final exact = token.kind == 'neuron' || token.kind == 'signal';
    for (final m in RegExp(
      RegExp.escape(token.name),
      caseSensitive: exact,
    ).allMatches(text)) {
      if (m.start > 0 && word(text[m.start - 1]) ||
          m.end < text.length && word(text[m.end])) {
        continue;
      }
      matches.add(SpecSpan(m.start, m.end, token.kind, [token]));
    }
  }
  const priority = {
    'operation': 0,
    'signal': 1,
    'neuron': 2,
    'setting': 3,
    'account': 3,
  };
  matches.sort((a, b) {
    final length = (b.end - b.start).compareTo(a.end - a.start);
    return length != 0
        ? length
        : (priority[a.kind] ?? 9).compareTo(priority[b.kind] ?? 9);
  });
  final accepted = <SpecSpan>[];
  for (final candidate in matches) {
    final same = accepted.indexWhere(
      (s) =>
          s.start == candidate.start &&
          s.end == candidate.end &&
          s.kind == candidate.kind,
    );
    if (same >= 0) {
      final previous = accepted[same];
      accepted[same] = SpecSpan(previous.start, previous.end, previous.kind, [
        ...previous.tokens,
        ...candidate.tokens,
      ]);
    } else if (!accepted.any(
      (s) => s.start < candidate.end && candidate.start < s.end,
    )) {
      accepted.add(candidate);
    }
  }
  for (final quote in RegExp(r'"[^"\n]*"|“[^”\n]*”').allMatches(text)) {
    if (!accepted.any((s) => s.start < quote.end && quote.start < s.end)) {
      accepted.add(SpecSpan(quote.start, quote.end, 'literal', const []));
    }
  }
  accepted.sort((a, b) => a.start.compareTo(b.start));
  return accepted;
}
