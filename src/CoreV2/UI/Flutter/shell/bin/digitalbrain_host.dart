import 'dart:async';
import 'dart:io';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';

Future<void> main() async {
  final productBase = DigitalBrainHostEnvironment.requireProductBase();
  stdout.writeln(
    'DigitalBrain CoreV2 headless host connected to ${productBase.origin}',
  );
  await Completer<void>().future;
}
