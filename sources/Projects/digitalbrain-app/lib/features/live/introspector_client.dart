import 'dart:convert';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';

// Wraps the gateway Send RPC for introspector synapse types.
// All requests go through the existing SynapseEnvelope/Send path so no new
// gRPC stubs are needed.
class IntrospectorClient {
  IntrospectorClient(this._gateway);

  final DigitalBrainGatewayClient _gateway;

  static const _nilGuid = '00000000-0000-0000-0000-000000000000';
  static const _receiverType = 'IntrospectorNeuron';

  // Asks the introspector to return the full synapse chain for a given correlationId.
  Future<List<SynapseSummary>> traceCorrelation(String correlationId) async {
    final payload = jsonEncode({
      'SynapseId':           _nilGuid,
      'CorrelationId':       _nilGuid,
      'CausationId':         null,
      'CallerNeuronId':      _nilGuid,
      'CallerNeuronType':    'External',
      'ReceiverNeuronId':    _nilGuid,
      'ReceiverNeuronType':  _receiverType,
      'Timestamp':           DateTime.now().toUtc().toIso8601String(),
      'TargetCorrelationId': correlationId,
    });
    final envelope = SynapseEnvelope()
      ..correlationId = ''
      ..typeName = 'DigitalBrain.Kernel.Contracts.Introspector.TraceCorrelationRequest'
      ..payload = Uint8List.fromList(utf8.encode(payload));

    final reply = await _gateway.send(envelope);
    final body = jsonDecode(utf8.decode(reply.payload)) as Map<String, dynamic>;
    final chain = (body['Chain'] ?? body['chain']) as List<dynamic>? ?? [];
    return chain
        .cast<Map<String, dynamic>>()
        .map(SynapseSummary.fromJson)
        .toList();
  }

  // Searches for neurons whose .feature text matches the query.
  Future<List<NeuronSearchResult>> findNeuronsByFeatureText(
      String query, int limit) async {
    final payload = jsonEncode({
      'SynapseId':          _nilGuid,
      'CorrelationId':      _nilGuid,
      'CausationId':        null,
      'CallerNeuronId':     _nilGuid,
      'CallerNeuronType':   'External',
      'ReceiverNeuronId':   _nilGuid,
      'ReceiverNeuronType': _receiverType,
      'Timestamp':          DateTime.now().toUtc().toIso8601String(),
      'Query': query,
      'Limit': limit,
    });
    final envelope = SynapseEnvelope()
      ..correlationId = ''
      ..typeName =
          'DigitalBrain.Kernel.Contracts.Introspector.FindNeuronsByFeatureTextRequest'
      ..payload = Uint8List.fromList(utf8.encode(payload));

    final reply = await _gateway.send(envelope);
    final body = jsonDecode(utf8.decode(reply.payload)) as Map<String, dynamic>;
    final neurons = (body['Neurons'] ?? body['neurons']) as List<dynamic>? ?? [];
    return neurons
        .cast<Map<String, dynamic>>()
        .map(NeuronSearchResult.fromJson)
        .toList();
  }

  // Searches for correlation chains whose conversational context matches [text].
  Future<List<String>> findChainsByConversationText(
    String text, {
    DateTime? since,
    DateTime? until,
    int limit = 20,
  }) async {
    final payload = jsonEncode({
      'SynapseId':          _nilGuid,
      'CorrelationId':      _nilGuid,
      'CausationId':        null,
      'CallerNeuronId':     _nilGuid,
      'CallerNeuronType':   'External',
      'ReceiverNeuronId':   _nilGuid,
      'ReceiverNeuronType': _receiverType,
      'Timestamp':          DateTime.now().toUtc().toIso8601String(),
      'Text':  text,
      'Since': since?.toUtc().toIso8601String(),
      'Until': until?.toUtc().toIso8601String(),
      'Limit': limit,
    });
    final envelope = SynapseEnvelope()
      ..correlationId = ''
      ..typeName =
          'DigitalBrain.Kernel.Contracts.Introspector.FindChainsByConversationTextRequest'
      ..payload = Uint8List.fromList(utf8.encode(payload));

    final reply = await _gateway.send(envelope);
    final body = jsonDecode(utf8.decode(reply.payload)) as Map<String, dynamic>;
    final ids = (body['CorrelationIds'] ?? body['correlationIds'])
            as List<dynamic>? ??
        [];
    return ids.cast<String>();
  }
}

// A single entry in a traced synapse chain.
class SynapseSummary {
  SynapseSummary({
    required this.synapseId,
    required this.correlationId,
    required this.typeName,
    required this.callerType,
    required this.receiverType,
    required this.timestamp,
  });

  final String synapseId;
  final String correlationId;
  final String typeName;
  final String? callerType;
  final String receiverType;
  final String timestamp;

  factory SynapseSummary.fromJson(Map<String, dynamic> j) => SynapseSummary(
        synapseId: (j['SynapseId'] ?? j['synapseId'] ?? '').toString(),
        correlationId:
            (j['CorrelationId'] ?? j['correlationId'] ?? '').toString(),
        typeName: (j['TypeName'] ?? j['typeName'] ?? '').toString(),
        callerType:
            (j['CallerNeuronType'] ?? j['callerNeuronType'])?.toString(),
        receiverType:
            (j['ReceiverNeuronType'] ?? j['receiverNeuronType'] ?? '')
                .toString(),
        timestamp: (j['Timestamp'] ?? j['timestamp'] ?? '').toString(),
      );
}

// A reference to a matching neuron from a feature-text search.
class NeuronSearchResult {
  NeuronSearchResult({
    required this.neuronType,
    required this.domain,
    this.featureSnippet,
  });

  final String neuronType;
  final String domain;
  final String? featureSnippet;

  factory NeuronSearchResult.fromJson(Map<String, dynamic> j) =>
      NeuronSearchResult(
        neuronType:
            (j['NeuronType'] ?? j['neuronType'] ?? '').toString(),
        domain: (j['Domain'] ?? j['domain'] ?? '').toString(),
        featureSnippet:
            (j['FeatureSnippet'] ?? j['featureSnippet'])?.toString(),
      );
}
