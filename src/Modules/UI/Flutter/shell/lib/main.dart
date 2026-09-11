import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'auth/brain_session_gate.dart';
import 'workspace/workspace_app.dart';
import 'workspace/workspace_store.dart';
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
  WorkspaceStore? workspaceStore,
}) {
  final scope = base64Url.encode(
    utf8.encode(
      edge == null
          ? 'offline'
          : '${edge.baseUri.origin}|${edge.workspaceIdentity}',
    ),
  );
  return WorkspaceApp(
    key: ValueKey(scope),
    persistenceKey: 'intocaht.workspace.v1.$scope',
    store: workspaceStore,
    kernelBaseUri: edge?.baseUri,
    onRun: edge?.runAgent,
    onCreateArtifact: edge?.createWorkspaceArtifact,
    onReadArtifact: edge?.readWorkspaceArtifact,
    onUpdateArtifact: edge?.updateWorkspaceArtifact,
    onListArtifacts: edge?.listWorkspaceArtifacts,
    onCreateTable: edge?.createTable,
    onReadBrain: edge == null ? null : () => edge.readBrain(chatName: chat),
    onWatchBrain: edge == null ? null : () => edge.watchBrain(chatName: chat),
    onTranscribe: edge == null
        ? null
        : (bytes, fileName) =>
              edge.transcribeVoice(audioBytes: bytes, fileName: fileName),
    onReadTable: edge?.readTable,
    onUpdateTableView: edge?.updateTableView,
    onListTables: edge?.listTables,
    statusMessage: statusMessage,
    onOpenUrl: openExternalUrl,
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
