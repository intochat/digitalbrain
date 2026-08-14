import 'package:digitalbrain_corev2_shell/main.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('shell identifies CoreV2 and its ProductHost origin', (tester) async {
    await tester.pumpWidget(
      DigitalBrainShell(productBase: Uri.parse('http://localhost:5100')),
    );

    expect(find.text('DigitalBrain CoreV2'), findsOneWidget);
    expect(find.text('http://localhost:5100'), findsOneWidget);
  });
}
