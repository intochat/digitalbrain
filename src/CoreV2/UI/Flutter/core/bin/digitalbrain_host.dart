import 'dart:async';
import 'dart:io';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';

Future<void> main() async {
  final baseUri = DigitalBrainHostEnvironment.requireProductBase();
  final shell = DigitalBrainHostEnvironment.resolveShell();
  final client = DigitalBrainProductClient(baseUri: baseUri);

  try {
    final modules = await client.getModules();
    final operations = await client.getOperations();
    var brain = await client.getBrain();
    stdout.writeln(
      'DigitalBrain headless Flutter host ready: '
      'shell=$shell modules=${modules.length} operations=${operations.length} '
      'neurons=${brain.neurons.length} synapses=${brain.synapses.length}',
    );
    final brainEvents = client.watchBrain(afterSequence: brain.sequence).listen((
      snapshot,
    ) {
      brain = snapshot;
      stdout.writeln(
        'BrainGraph sequence=${snapshot.sequence} neurons=${snapshot.neurons.length} '
        'synapses=${snapshot.synapses.length}',
      );
    });

    final stopped = Completer<void>();
    final heartbeat = Timer.periodic(
      const Duration(seconds: 30),
      (_) => stdout.writeln('DigitalBrain headless Flutter host healthy'),
    );
    final interrupt = ProcessSignal.sigint.watch().listen((_) {
      if (!stopped.isCompleted) {
        stopped.complete();
      }
    });

    await stopped.future;
    heartbeat.cancel();
    await brainEvents.cancel();
    await interrupt.cancel();
  } finally {
    client.close();
  }
}
