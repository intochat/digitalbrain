import 'package:flutter/widgets.dart';

mixin PresentOnce<T extends StatefulWidget> on State<T> {
  bool _presented = false;

  void presentOnce(bool shouldPresent, void Function() present) {
    if (!shouldPresent) {
      _presented = false;
      return;
    }
    if (_presented) return;
    _presented = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) present();
    });
  }
}
