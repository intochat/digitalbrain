import 'package:flutter/material.dart';

class AnimSpec {
  const AnimSpec({required this.duration, this.curve = Curves.easeOut});
  final Duration duration;
  final Curve curve;

  @override
  bool operator ==(Object other) =>
      other is AnimSpec && other.duration == duration && other.curve == curve;

  @override
  int get hashCode => Object.hash(duration, curve);
}

const _curvesByName = <String, Curve>{
  'linear': Curves.linear,
  'easeIn': Curves.easeIn,
  'easeOut': Curves.easeOut,
  'easeInOut': Curves.easeInOut,
  'easeOutCubic': Curves.easeOutCubic,
};

AnimSpec? animSpecFromBindings({int? durMs, String? curve}) {
  if (durMs == null || durMs <= 0) return null;
  return AnimSpec(
    duration: Duration(milliseconds: durMs),
    curve: _curvesByName[curve] ?? Curves.easeOut,
  );
}

abstract interface class ViewModelHandle {
  void writeString(String name, String value);
  void writeNumber(String name, double value, {AnimSpec? anim});
  void writeColor(String name, Color value, {AnimSpec? anim});
  void writeEnum(String name, String value);
  void onTrigger(String name, VoidCallback handler);
  void dispose();
}

abstract interface class RiveResolution {
  ViewModelHandle get viewModel;
  Widget buildWidget();
  void dispose();
}
