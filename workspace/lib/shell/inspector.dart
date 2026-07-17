import 'package:flutter/material.dart';

import '../gateway/brain_gateway.dart';
import '../gateway/envelope.dart';
import '../theme/brain_theme.dart';

class Inspector extends StatelessWidget {
  const Inspector(this.gateway, this.address, {super.key});

  final BrainGateway gateway;
  final String? address;

  @override
  Widget build(BuildContext context) {
    final currentAddress = address;
    if (currentAddress == null) {
      return const Center(child: Text('Select something to inspect.'));
    }
    return FutureBuilder<(NeuronDescription, NeuronSnapshot)>(
      future: _load(currentAddress),
      builder: (context, snapshot) {
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            _section('Status', _statusContent(snapshot)),
            const SizedBox(height: 16),
            _section('Caused by', const Text('—')),
            const SizedBox(height: 16),
            _section('Depends on', const Text('—')),
            const SizedBox(height: 16),
            _section('Actions', const Text('—')),
          ],
        );
      },
    );
  }

  Future<(NeuronDescription, NeuronSnapshot)> _load(String address) async {
    final description = await gateway.describe(address);
    final snapshot = await gateway.read(address);
    return (description, snapshot);
  }

  Widget _statusContent(
    AsyncSnapshot<(NeuronDescription, NeuronSnapshot)> snapshot,
  ) {
    if (snapshot.connectionState != ConnectionState.done) {
      return const SizedBox(
        height: 20,
        width: 20,
        child: CircularProgressIndicator(strokeWidth: 2),
      );
    }
    if (snapshot.hasError) {
      return Text(
        'Could not inspect: ${snapshot.error}',
        style: const TextStyle(color: BrainColors.orange),
      );
    }
    final (description, neuronSnapshot) = snapshot.data!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('kind: ${description.kind}', style: BrainTheme.mono(null)),
        Text(
          'revision: ${neuronSnapshot.revision}',
          style: BrainTheme.mono(null),
        ),
        Text(
          'contracts: ${description.contracts.join(', ')}',
          style: BrainTheme.mono(null),
        ),
      ],
    );
  }

  Widget _section(String title, Widget content) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: const TextStyle(
            fontWeight: FontWeight.w600,
            color: BrainColors.inkMuted,
          ),
        ),
        const SizedBox(height: 8),
        content,
      ],
    );
  }
}
