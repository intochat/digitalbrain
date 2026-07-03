import 'package:flutter/material.dart';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';

// Threads the gRPC gateway client down through the widget tree so deeply
// nested RFW cards (e.g. OptionChipStackCard, PlanCard) can fire synapses
// without callback drilling. HomeScreen wraps its body in this once.
class DigitalBrainClientScope extends InheritedWidget {
  const DigitalBrainClientScope({
    super.key,
    required this.client,
    required super.child,
  });

  final DigitalBrainGatewayClient client;

  static DigitalBrainGatewayClient? of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<DigitalBrainClientScope>();
    return scope?.client;
  }

  @override
  bool updateShouldNotify(DigitalBrainClientScope oldWidget) =>
      !identical(client, oldWidget.client);
}
