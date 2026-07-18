import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const Key _goldenBoundaryKey = Key('digitalbrain-shell-golden-boundary');

void main() {
  testWidgets('compact shell drawer at 320 pixels', (tester) async {
    await _pumpGolden(tester, size: const Size(320, 720));
    await tester.tap(find.byKey(digitalBrainOpenNavigationKey));
    await tester.pumpAndSettle();

    await expectLater(
      find.byKey(_goldenBoundaryKey),
      matchesGoldenFile('../goldens/digitalbrain_shell_compact.png'),
    );
  });

  testWidgets('collapsed shell rail at 736 pixels', (tester) async {
    await _pumpGolden(tester, size: const Size(736, 900));

    await expectLater(
      find.byKey(_goldenBoundaryKey),
      matchesGoldenFile('../goldens/digitalbrain_shell_medium.png'),
    );
  });

  testWidgets('extended Studio shell at 1440 pixels', (tester) async {
    await _pumpGolden(
      tester,
      size: const Size(1440, 900),
      location: Uri.parse(
        '/features/proposals/proposal-0123456789abcdef0123456789abcdef',
      ),
      studio: true,
    );

    await expectLater(
      find.byKey(_goldenBoundaryKey),
      matchesGoldenFile('../goldens/digitalbrain_shell_large.png'),
    );
  });
}

Future<void> _pumpGolden(
  WidgetTester tester, {
  required Size size,
  Uri? location,
  bool studio = false,
}) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  final theme = ThemeData(
    brightness: Brightness.dark,
    colorSchemeSeed: const Color(0xffe5e5e5),
    scaffoldBackgroundColor: Colors.black,
    useMaterial3: true,
  );

  await tester.pumpWidget(
    RepaintBoundary(
      key: _goldenBoundaryKey,
      child: MaterialApp(
        debugShowCheckedModeBanner: false,
        theme: theme,
        darkTheme: theme,
        themeMode: ThemeMode.dark,
        home: WindowSizeScope(
          child: DigitalBrainShell(
            location: location ?? Uri.parse('/chat'),
            onDestinationSelected: (_) {},
            onSignOut: () {},
            child: _GoldenCanvas(studio: studio),
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

class _GoldenCanvas extends StatelessWidget {
  const _GoldenCanvas({required this.studio});

  final bool studio;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ColoredBox(
      color: theme.scaffoldBackgroundColor,
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 720),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: studio
                      ? _studioContent(theme)
                      : _chatContent(theme),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _chatContent(ThemeData theme) => [
    Icon(Icons.auto_awesome, color: theme.colorScheme.primary),
    const SizedBox(height: 16),
    Text('Start with a request', style: theme.textTheme.headlineSmall),
    const SizedBox(height: 8),
    const Text('Chat keeps the work and its context together.'),
    const SizedBox(height: 24),
    const TextField(
      readOnly: true,
      decoration: InputDecoration(
        hintText: 'Ask DigitalBrain to help…',
        suffixIcon: Icon(Icons.arrow_upward),
      ),
    ),
  ];

  List<Widget> _studioContent(ThemeData theme) => [
    Icon(Icons.architecture_outlined, color: theme.colorScheme.primary),
    const SizedBox(height: 16),
    Text('Draft created from Chat', style: theme.textTheme.headlineSmall),
    const SizedBox(height: 8),
    const Text('Review the Feature draft before continuing the build.'),
    const SizedBox(height: 24),
    const Text('draft-0123456789abcdef0123456789abcdef'),
    const SizedBox(height: 20),
    const Align(
      alignment: Alignment.centerRight,
      child: OutlinedButton(onPressed: null, child: Text('Back to Chat')),
    ),
  ];
}
