import 'dart:convert';

import 'package:flutter/material.dart';

import '../gateway/brain_gateway.dart';
import '../gateway/envelope.dart';
import '../theme/brain_theme.dart';

class ActivityPage extends StatelessWidget {
  const ActivityPage(this.gateway, {super.key});

  final BrainGateway gateway;

  static const String _address = 'local-owner|actor/ui-dev|feed/main';

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<NeuronSnapshot>(
      future: gateway.read(_address),
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return const Center(child: Text('Activity is unavailable.'));
        }
        final records = _records(snapshot.data!.stateJson);
        if (records.isEmpty) {
          return const Center(child: Text('No activity yet.'));
        }
        return ListView(
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: records.map(_row).toList(),
        );
      },
    );
  }

  List<Map<String, dynamic>> _records(String stateJson) {
    try {
      final decoded = jsonDecode(stateJson);
      if (decoded is! Map<String, dynamic>) return const [];
      final rawRecords = decoded['records'];
      if (rawRecords is! List) return const [];
      return rawRecords.whereType<Map<String, dynamic>>().toList();
    } on FormatException {
      return const [];
    }
  }

  Widget _row(Map<String, dynamic> record) {
    final kind = record['kind']?.toString() ?? '';
    final sourceKey = record['sourceKey']?.toString() ?? '';
    final revision = record['revision']?.toString() ?? '';
    return ListTile(
      leading: Chip(label: Text(kind)),
      title: Text(sourceKey, style: BrainTheme.mono(null)),
      trailing: Text('rev $revision', style: BrainTheme.mono(null)),
    );
  }
}
