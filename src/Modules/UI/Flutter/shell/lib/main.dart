import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'auth/brain_session_gate.dart';
import 'chat_screen.dart';
import 'open_url_io.dart'
    if (dart.library.html) 'open_url_web.dart'
    as open_url;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final chat = DigitalBrainHostEnv.resolveChat();

  // The gate owns client construction now: it must hold the credentials the
  // kernel accepted before any stream opens.
  runApp(
    BrainSessionGate(
      builder: (client, status) =>
          buildShell(chat: chat, edge: client, statusMessage: status),
    ),
  );
}

@visibleForTesting
Widget buildShell({
  required String chat,
  required DigitalBrainUiClient? edge,
  String? statusMessage,
}) {
  return BrainChatApp(
    client: edge,
    chatName: chat,
    statusMessage: statusMessage,
    turns: edge?.watchChatTurns(chatName: chat),
    onSend: edge == null
        ? null
        : (text) => edge.sendMessage(chatName: chat, text: text),
    onSendVoice: edge == null
        ? null
        : (audioBytes, {fileName = 'voice.wav'}) => edge.sendVoice(
            chatName: chat,
            audioBytes: audioBytes,
            fileName: fileName,
          ),
    onOpenSignIn: openExternalUrl,
    kernelBaseUri: edge?.baseUri,
    onCancelTurn: edge == null
        ? null
        : ({required turnId}) =>
              edge.cancelTurn(chatName: chat, turnId: turnId),
    onReadChart: edge?.readChart,
    onReadImageBytes: edge?.readImageBytes,
    onReadSpreadsheet: edge?.readSpreadsheet,
    onReadGraph: edge?.readGraph,
    onReadSurface: edge?.readSurface,
    surfaceEvents: edge?.watchSurfaceEvents(surfaceName: 'desk'),
    onWatchActivities: edge == null
        ? null
        : () => edge.watchActivities(surfaceName: 'desk'),
    onReadBrain: edge == null ? null : () => edge.readBrain(chatName: chat),
    onWatchBrain: edge == null ? null : () => edge.watchBrain(chatName: chat),
    onSetBrainSubscription: edge == null
        ? null
        : ({
            required sourceId,
            required targetId,
            required signalType,
            required subscribed,
          }) => edge.setBrainSubscription(
            chatName: chat,
            sourceId: sourceId,
            targetId: targetId,
            signalType: signalType,
            subscribed: subscribed,
          ),
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
