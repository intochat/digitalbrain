import 'dart:async';

import 'package:digital_brain_sdk_flutter/digital_brain_sdk_flutter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('completed watch hint stream backs off instead of spinning', () async {
    var watchCalls = 0;
    final stream = await PerfStream.bootstrap(
      gateway: PerfGatewayClient(
        pushSamples: (samples) => samples.drain<void>(),
        watchHints: (_) {
          watchCalls++;
          return const Stream<PerfTierHint>.empty();
        },
      ),
    );

    await Future<void>.delayed(const Duration(milliseconds: 50));
    await stream.dispose();

    expect(watchCalls, 1);
  });
}
