import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/kinds/effect_preview.dart';

void main() {
  testWidgets('renders the summary and a truncated payload digest', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: EffectPreview(
            data: {
              'summary': 'Will send 3 emails',
              'payloadDigest': 'sha256:abcdefabcdefabcdefabcdefabcdef',
            },
          ),
        ),
      ),
    );

    expect(find.text('Will send 3 emails'), findsOneWidget);
    expect(find.text('sha256:abcdefabcdefa…'), findsOneWidget);
  });

  testWidgets(
    'renders the digest in full when shorter than the truncation limit',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: EffectPreview(
              data: {
                'summary': 'Will send 1 email',
                'payloadDigest': 'sha256:short',
              },
            ),
          ),
        ),
      );

      expect(find.text('sha256:short'), findsOneWidget);
    },
  );
}
