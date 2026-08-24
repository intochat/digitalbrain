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
    chatName: chat,
    statusMessage: statusMessage,
    turns: edge?.watchChatTurns(chatName: chat),
    onStream: edge == null
        ? null
        : (text) => edge.streamMessage(chatName: chat, text: text),
    onStreamVoice: edge == null
        ? null
        : (audioBytes, {fileName = 'voice.wav'}) => edge.streamVoice(
            chatName: chat,
            audioBytes: audioBytes,
            fileName: fileName,
          ),
    onActivateButton: edge == null
        ? null
        : ({required offerCommandId, required buttonId, required action}) =>
              edge.activateChatButton(
                chatName: chat,
                offerCommandId: offerCommandId,
                buttonId: buttonId,
                action: action,
              ),
    onOpenSignIn: openExternalUrl,
    onReadChart: edge?.readChart,
    onReadImageBytes: edge?.readImageBytes,
    onLoadBehaviors: edge?.listBehaviors,
    onLoadBehaviorSteps: edge?.listBehaviorSteps,
    onSaveBehavior: edge?.saveBehavior,
    onTestBehavior: edge?.testBehavior,
    onActivateBehavior: edge?.activateBehavior,
    onRunBehaviorFake: edge?.runBehaviorFake,
    onGenerateBehavior: edge?.generateBehavior,
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
