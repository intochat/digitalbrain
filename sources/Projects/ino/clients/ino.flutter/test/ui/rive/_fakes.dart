import 'package:flutter/material.dart';
import 'package:ino_flutter/ui/rive/rive_handles.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rive/rive.dart' as rive;

class FakeRiveFile extends Fake implements rive.File {
  @override
  void dispose() {}
}

class FakeBindableArtboard extends Fake implements rive.BindableArtboard {
  @override
  void dispose() {}
}

// RiveWidgetController is a `base` class — cannot be implemented outside its
// library. Downstream tests that need a controller seam must use a real
// RiveWidgetController (loaded from a real .riv) or restructure the widget
// under test to accept an abstraction. See Task 2 notes.

class MockViewModelInstance extends Mock implements rive.ViewModelInstance {}

class MockNumberProperty extends Mock implements rive.ViewModelInstanceNumber {}

class MockStringProperty extends Mock implements rive.ViewModelInstanceString {}

class MockColorProperty extends Mock implements rive.ViewModelInstanceColor {}

class MockTriggerProperty extends Mock
    implements rive.ViewModelInstanceTrigger {}

class MockEnumProperty extends Mock implements rive.ViewModelInstanceEnum {}

class MockViewModelHandle extends Mock implements ViewModelHandle {}

class MockRiveResolution extends Mock implements RiveResolution {}

void registerRiveFallbacks() {
  registerFallbackValue(const Color(0xFF000000));
}
