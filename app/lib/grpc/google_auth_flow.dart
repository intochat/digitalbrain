import 'dart:convert';

import 'package:url_launcher/url_launcher.dart';

import 'digitalbrain.pb.dart' as gw;

const googleAuthUrlSignal = 'GoogleAuthUrl';
const salesforceAuthUrlSignal = 'SalesforceAuthUrl';
const _authUrlSignals = {googleAuthUrlSignal, salesforceAuthUrlSignal};

gw.WatchSynapsesRequest googleAuthUrlWatchRequest() => authUrlWatchRequest();

gw.WatchSynapsesRequest authUrlWatchRequest() =>
    gw.WatchSynapsesRequest(typeFilter: _authUrlSignals.toList());

String? googleAuthUrlFromEnvelope(gw.SynapseEnvelope envelope) =>
    authUrlFromEnvelope(envelope);

String? authUrlFromEnvelope(gw.SynapseEnvelope envelope) {
  if (!_authUrlSignals.contains(envelope.typeName)) return null;

  try {
    final decoded = jsonDecode(utf8.decode(envelope.payload));
    if (decoded is Map) {
      final url = decoded['url']?.toString().trim();
      return url == null || url.isEmpty ? null : url;
    }
  } catch (_) {}

  return null;
}

Future<bool> openGoogleAuthUrlFromEnvelope(gw.SynapseEnvelope envelope) async {
  return openAuthUrlFromEnvelope(envelope);
}

Future<bool> openAuthUrlFromEnvelope(gw.SynapseEnvelope envelope) async {
  final url = authUrlFromEnvelope(envelope);
  final uri = url == null ? null : Uri.tryParse(url);
  if (uri == null) return false;

  return launchUrl(uri, mode: LaunchMode.externalApplication);
}
