import 'dart:convert';

import 'package:flutter/material.dart';

import '../gateway/brain_gateway.dart';
import '../gateway/envelope.dart';

class AbilitiesPage extends StatelessWidget {
  const AbilitiesPage(this.gateway, {super.key});

  final BrainGateway gateway;

  static const String _address = 'catalog/main';

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<NeuronSnapshot>(
      future: gateway.read(_address),
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return const Center(child: Text('Coming in a later slice.'));
        }
        final kinds = _kinds(snapshot.data!.stateJson);
        if (kinds.isEmpty) {
          return const Center(child: Text('Coming in a later slice.'));
        }
        return ListView(
          children: kinds.map((kind) => ListTile(title: Text(kind))).toList(),
        );
      },
    );
  }

  List<String> _kinds(String stateJson) {
    try {
      final decoded = jsonDecode(stateJson);
      if (decoded is! Map<String, dynamic>) return const [];
      final rawKinds = decoded['kinds'];
      if (rawKinds is! List) return const [];
      return rawKinds.map((kind) => kind.toString()).toList();
    } on FormatException {
      return const [];
    }
  }
}
