import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'brain_chat_screen.dart';
import 'chat_contracts.dart';

/// Graph canvas with the assistant docked at the bottom.
final class GraphHomeScreen extends StatelessWidget {
  const GraphHomeScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onActivateButton,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ActivateChatButton? onActivateButton;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;

  @override
  Widget build(BuildContext context) {
    final graph = graphFromTurns(turns);
    return ColoredBox(
      key: const Key('graph_home_screen'),
      color: BrainPalette.surfaceSunken,
      child: Column(
        children: [
          Expanded(
            flex: 3,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: BrainPalette.surface,
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: BrainPalette.line),
                ),
                child: KitGraph(
                  nodes: graph.nodes,
                  edges: graph.edges,
                  pulse: graph.pulse,
                ),
              ),
            ),
          ),
          const Padding(
            padding: EdgeInsets.symmetric(horizontal: 20),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text('Assistant', style: BrainType.metaStrong),
            ),
          ),
          Expanded(
            flex: 2,
            child: BrainChatScreen(
              chatName: chatName,
              turns: turns,
              onSend: onSend,
              onStream: onStream,
              onStreamVoice: onStreamVoice,
              onAttachmentTap: onAttachmentTap,
              onOpenSignIn: onOpenSignIn,
              kernelBaseUri: kernelBaseUri,
              onCancelTurn: onCancelTurn,
              onActivateButton: onActivateButton,
              onReadChart: onReadChart,
              onReadImageBytes: onReadImageBytes,
              onReadSpreadsheet: onReadSpreadsheet,
              onReadGraph: onReadGraph,
            ),
          ),
        ],
      ),
    );
  }
}

final class GraphViewModel {
  const GraphViewModel({
    required this.nodes,
    required this.edges,
    this.pulse,
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final GraphPulse? pulse;
}

GraphViewModel graphFromTurns(List<ChatTurnEvent> turns) {
  const brain = GraphNode(id: 'brain', label: 'DigitalBrain', kind: GraphNodeKind.hub);
  const chat = GraphNode(id: 'chat', label: 'chat');
  final nodes = <GraphNode>[brain, chat];
  final edges = <GraphEdge>[
    const GraphEdge(id: 'brain-chat', sourceId: 'brain', targetId: 'chat'),
  ];

  for (final turn in turns) {
    for (final card in turn.cards) {
      if (nodes.any((node) => node.id == card.name)) {
        continue;
      }
      nodes.add(GraphNode(id: card.name, label: card.caption));
      edges.add(
        GraphEdge(
          id: 'chat-${card.name}',
          sourceId: 'chat',
          targetId: card.name,
          decorated: card.kind == 'spreadsheet' || card.kind == 'chart',
        ),
      );
    }
  }

  GraphPulse? pulse;
  if (turns.isNotEmpty) {
    final last = turns.last;
    final to = last.cards.isNotEmpty ? last.cards.last.name : 'chat';
    pulse = GraphPulse(
      fromId: last.fromUser ? 'chat' : 'brain',
      toId: to,
      signature: '${last.sequence}:${last.commandId}',
    );
  }

  return GraphViewModel(nodes: nodes, edges: edges, pulse: pulse);
}
