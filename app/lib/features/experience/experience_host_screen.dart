import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

import 'experience_hop_view.dart';
import 'experience_match.dart';

/// Full-screen host for a guided experience. Subscribes to WatchHomeFeed and renders the latest
/// hop whose surface carries a matching `activeExperience` marker, replacing the previous hop in
/// place as the journey advances. Chrome-free except a minimal exit affordance.
class ExperienceHostScreen extends StatefulWidget {
  const ExperienceHostScreen({super.key, this.pack, this.experienceId});

  final String? pack;
  final String? experienceId;

  String? get target =>
      (pack != null && experienceId != null) ? '$pack/$experienceId' : null;

  @override
  State<ExperienceHostScreen> createState() => _ExperienceHostScreenState();
}

class _ExperienceHostScreenState extends State<ExperienceHostScreen> {
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  dynamic _channel;
  DigitalBrainGatewayClient? _client;
  StreamSubscription<gw.RfwCardEnvelope>? _feedSub;
  bool _startFired = false;

  Map<String, Object?>? _hopData;
  String? _hopCorrelationId;
  String? _status;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    _feedSub?.cancel();
    _channel?.shutdown();
    super.dispose();
  }

  void _connect() {
    try {
      final (host, port, secure) = resolveKernelEndpoint();
      final channel = createKernelChannel(host: host, port: port, secure: secure);
      final client = DigitalBrainGatewayClient(channel, interceptors: kernelInterceptors());
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest())
          .listen(_onCard, onError: _onError);
      setState(() {
        _channel = channel;
        _client = client;
        _feedSub = sub;
        _status = 'Waiting for the experience to start…';
      });
    } catch (error) {
      setState(() => _status = 'Experience feed connection failed: $error');
    }
  }

  void _onError(Object error, StackTrace _) {
    if (!mounted) return;
    setState(() => _status = 'Experience feed error: $error');
  }

  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    if (!_startFired && widget.target != null) {
      _startFired = true;
      _fireStart();
    }
    final data = _decode(envelope.dataJson);
    if (!experienceHopMatches(data, widget.target)) return;
    setState(() {
      _hopData = data;
      _hopCorrelationId = envelope.correlationId;
      _status = null;
    });
  }

  Map<String, Object?> _decode(String json) {
    try {
      final d = jsonDecode(json);
      if (d is Map) return d.map((k, v) => MapEntry(k.toString(), v));
    } catch (_) {}
    return const {};
  }

  void _onSurfaceEvent(String name, Map<String, Object?> args) {
    final envelope = buildActionEnvelope(name, args);
    final client = _client;
    if (envelope == null || client == null) return;
    client.send(envelope).then(
      (_) {},
      onError: (Object error) => _onError(error, StackTrace.current),
    );
  }

  void _fireStart() {
    final pack = widget.pack;
    final experienceId = widget.experienceId;
    final client = _client;
    if (pack == null || experienceId == null || client == null) return;
    final envelope = gw.SynapseEnvelope()
      ..correlationId = 'start-$pack'
      ..typeName = 'ExperienceStep'
      ..payload = utf8.encode(jsonEncode({
        'pack': pack,
        'experienceId': experienceId,
        'eventName': 'start',
      }));
    client.send(envelope).then(
      (_) {},
      onError: (Object error) => _onError(error, StackTrace.current),
    );
  }

  @override
  Widget build(BuildContext context) {
    final data = _hopData;
    final correlationId = _hopCorrelationId;
    final body = (data != null && correlationId != null)
        ? ExperienceHopView(
            host: _rfwHost,
            data: data,
            correlationId: correlationId,
            onEvent: _onSurfaceEvent,
          )
        : Center(child: Text(_status ?? 'Loading experience…'));

    return Scaffold(
      body: SafeArea(
        child: Stack(
          children: [
            Positioned.fill(child: body),
            Positioned(
              left: 8,
              top: 8,
              child: BackButton(
                onPressed: () =>
                    context.canPop() ? context.pop() : context.go('/'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
