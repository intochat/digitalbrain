import 'dart:convert';

import 'package:url_launcher/url_launcher.dart';

import 'digitalbrain.pb.dart' as gw;

const googleAuthUrlSignal = 'GoogleAuthUrl';

gw.WatchSynapsesRequest googleAuthUrlWatchRequest() =>
    gw.WatchSynapsesRequest(typeFilter: const [googleAuthUrlSignal]);

String? googleAuthUrlFromEnvelope(gw.SynapseEnvelope envelope) {
  if (envelope.typeName != googleAuthUrlSignal) return null;

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
  final url = googleAuthUrlFromEnvelope(envelope);
  final uri = url == null ? null : Uri.tryParse(url);
  if (uri == null) return false;

  return launchUrl(uri, mode: LaunchMode.externalApplication);
}
