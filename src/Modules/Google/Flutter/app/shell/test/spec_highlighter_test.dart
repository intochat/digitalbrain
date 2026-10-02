import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/spec_highlighter.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/spec_vocabulary.dart';

void main() {
  test(
    'registry matching preserves boundaries, ambiguity, and quoted operations',
    () {
      final tokens = [
        SpecToken(name: 'research', kind: 'operation'),
        SpecToken(name: 'IButton', kind: 'neuron', qualifiedName: 'A.IButton'),
        SpecToken(name: 'IButton', kind: 'neuron', qualifiedName: 'B.IButton'),
      ];
      const text = 'researcher "research" IButton ibutton "Stopped."';
      final spans = highlightSpec(text, tokens);
      expect(spans.where((s) => s.kind == 'operation').length, 1);
      expect(spans.singleWhere((s) => s.kind == 'neuron').tokens.length, 2);
      expect(spans.where((s) => s.kind == 'literal').length, 1);
      expect(highlightSpec('plain', []), isEmpty);
    },
  );
}
