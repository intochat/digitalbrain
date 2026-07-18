import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/composed_view.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('ComposedView mounts a Hero from the embedded sample',
      (tester) async {
    final registry = MockRegistry();
    final resolution = MockRiveResolution();
    final viewModel = MockViewModelHandle();

    when(() => registry.resolveController(
          domain: 'kernel',
          artboard: 'Hero',
        )).thenAnswer((_) async => resolution);
    when(() => resolution.viewModel).thenReturn(viewModel);
    when(() => resolution.buildWidget()).thenReturn(const SizedBox.shrink());
    when(() => resolution.dispose()).thenReturn(null);
    when(() => viewModel.writeString(any(), any())).thenReturn(null);
    when(() => viewModel.dispose()).thenReturn(null);

    await tester.pumpWidget(MaterialApp(
      home: ComposedView.sample(registry: registry),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(() => viewModel.writeString('title', 'Tokyo')).called(1);
  });
}
