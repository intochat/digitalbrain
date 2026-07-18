import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart' as gw;
import '../visual_constructor_models.dart';
import '../visual_constructor_state.dart';

class InspectorPanel extends StatefulWidget {
  final VisualConstructorState state;
  final VisualNode node;
  final gw.DigitalBrainGatewayClient? gatewayClient;

  const InspectorPanel({
    super.key,
    required this.state,
    required this.node,
    this.gatewayClient,
  });

  @override
  State<InspectorPanel> createState() => _InspectorPanelState();
}

class _InspectorPanelState extends State<InspectorPanel> {
  late TextEditingController _labelController;
  late TextEditingController _payloadController;

  @override
  void initState() {
    super.initState();
    _labelController = TextEditingController(text: widget.node.label);
    _payloadController = TextEditingController(text: widget.node.codePayload);
  }

  @override
  void didUpdateWidget(covariant InspectorPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.node.id != widget.node.id) {
      _labelController.text = widget.node.label;
      _payloadController.text = widget.node.codePayload;
    }
  }

  @override
  void dispose() {
    _labelController.dispose();
    _payloadController.dispose();
    super.dispose();
  }

  String _generateUuid() {
    final random = Random();
    final bytes = List<int>.generate(16, (i) => random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0F) | 0x40;
    bytes[8] = (bytes[8] & 0x3F) | 0x80;
    final chars = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).toList();
    return '${chars.sublist(0, 4).join()}-${chars.sublist(4, 6).join()}-${chars.sublist(6, 8).join()}-${chars.sublist(8, 10).join()}-${chars.sublist(10, 16).join()}';
  }

  void _showFireSynapseDialog() {
    final textCtrl = TextEditingController(
      text: "Alert! High-priority critical threat detected in data pipeline.",
    );
    final langCtrl = TextEditingController(text: "Spanish");

    showDialog(
      context: context,
      builder: (ctx) {
        return BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
          child: AlertDialog(
            backgroundColor: Colors.black87,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(24),
              side: BorderSide(color: const Color(0xFF00FFD2).withOpacity(0.3), width: 1),
            ),
            title: Row(
              children: [
                const Icon(Icons.flash_on, color: Color(0xFF00FFD2)),
                const SizedBox(width: 8),
                Text(
                  'Fire Synapse Signal',
                  style: GoogleFonts.outfit(
                    color: Colors.white,
                    fontWeight: FontWeight.bold,
                    fontSize: 18,
                  ),
                ),
              ],
            ),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'This dispatches a TranslateTextRequest synapse to the LlmTranslationNeuron, starting the LLM translation and sentiment analysis chain.',
                  style: GoogleFonts.outfit(color: Colors.white54, fontSize: 12),
                ),
                const SizedBox(height: 16),
                Text(
                  'SOURCE TEXT',
                  style: GoogleFonts.jetBrainsMono(color: Colors.white30, fontSize: 9, fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 6),
                TextField(
                  controller: textCtrl,
                  maxLines: 3,
                  style: const TextStyle(color: Colors.white, fontSize: 13),
                  decoration: InputDecoration(
                    fillColor: Colors.white.withOpacity(0.03),
                    filled: true,
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: const BorderSide(color: Colors.white12),
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: const BorderSide(color: Color(0xFF00FFD2)),
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Text(
                  'TARGET LANGUAGE',
                  style: GoogleFonts.jetBrainsMono(color: Colors.white30, fontSize: 9, fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 6),
                TextField(
                  controller: langCtrl,
                  style: const TextStyle(color: Colors.white, fontSize: 13),
                  decoration: InputDecoration(
                    fillColor: Colors.white.withOpacity(0.03),
                    filled: true,
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: const BorderSide(color: Colors.white12),
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: const BorderSide(color: Color(0xFF00FFD2)),
                    ),
                  ),
                ),
              ],
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: Text('Cancel', style: GoogleFonts.outfit(color: Colors.white54)),
              ),
              ElevatedButton(
                onPressed: () async {
                  Navigator.pop(ctx);
                  await _dispatchSynapse(textCtrl.text, langCtrl.text);
                },
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFF00FFD2),
                  foregroundColor: Colors.black,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                ),
                child: Text('Fire!', style: GoogleFonts.outfit(fontWeight: FontWeight.bold)),
              ),
            ],
          ),
        );
      },
    );
  }

  Future<void> _dispatchSynapse(String text, String language) async {
    final client = widget.gatewayClient;
    final correlationId = _generateUuid();

    // 1. Instantly trigger visual pulse on the local canvas (Point-to-Point pulse start)
    widget.state.onSynapseFiredLocally?.call("DigitalBrain.SDK.Ai.Contracts.TranslateTextRequest");

    if (client == null) {
      debugPrint("InspectorPanel: Offline mode. Simulated visual pulses triggered locally.");
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Offline mode: Visual pulses fired locally.'),
          backgroundColor: Color(0xFF00FFD2),
        ),
      );
      // Simulate point-to-point step 2 and broadcast ripple locally after brief delays for offline wow factor!
      Future.delayed(const Duration(milliseconds: 1000), () {
        if (mounted) {
          widget.state.onSynapseFiredLocally?.call("DigitalBrain.SDK.Ai.Contracts.TextTranslatedEvent");
        }
      });
      Future.delayed(const Duration(milliseconds: 2000), () {
        if (mounted) {
          widget.state.onSynapseFiredLocally?.call("DigitalBrain.SDK.Ai.Contracts.SystemAlertFiredEvent");
        }
      });
      return;
    }

    try {
      final envelope = gw.SynapseEnvelope()
        ..correlationId = correlationId
        ..typeName = 'DigitalBrain.SDK.Ai.Contracts.TranslateTextRequest'
        ..payload = Uint8List.fromList(utf8.encode(jsonEncode({
          'Text': text,
          'TargetLanguage': language,
        })));

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Dispatched TranslateTextRequest to Orleans grains...'),
          duration: Duration(seconds: 1),
        ),
      );

      await client.send(envelope);
    } catch (e) {
      debugPrint("InspectorPanel gRPC dispatch error: $e");
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error dispatching synapse: $e')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.6), // Frosted obsidian glass
        borderRadius: BorderRadius.circular(24.0),
        border: Border.all(
          color: Colors.white.withValues(alpha: 0.08),
          width: 1.0,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.3),
            blurRadius: 30,
            spreadRadius: 2,
          ),
        ],
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(24.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Panel Header
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
              decoration: BoxDecoration(
                color: Colors.white.withValues(alpha: 0.02),
                border: Border(
                  bottom: BorderSide(
                    color: Colors.white.withValues(alpha: 0.05),
                  ),
                ),
              ),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Row(
                    children: [
                      const Icon(
                        Icons.tune,
                        size: 16,
                        color: Color(0xFFFF9F1C),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        'Inspector',
                        style: TextStyle(
                          color: Colors.white.withValues(alpha: 0.9),
                          fontSize: 14,
                          fontWeight: FontWeight.w600,
                          letterSpacing: 0.5,
                        ),
                      ),
                    ],
                  ),
                  InkWell(
                    onTap: () => widget.state.selectNode(null),
                    borderRadius: BorderRadius.circular(12),
                    child: Container(
                      padding: const EdgeInsets.all(4),
                      decoration: BoxDecoration(
                        color: Colors.white.withValues(alpha: 0.03),
                        shape: BoxShape.circle,
                      ),
                      child: const Icon(
                        Icons.close,
                        size: 14,
                        color: Colors.white60,
                      ),
                    ),
                  ),
                ],
              ),
            ),

            // Scrollable Content
            Expanded(
              child: ListView(
                padding: const EdgeInsets.all(20),
                children: [
                  // Direct trigger Button (if Translation Neuron is selected)
                  if (widget.node.id == 'llm_translation_neuron') ...[
                    ElevatedButton.icon(
                      onPressed: _showFireSynapseDialog,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: const Color(0xFF00FFD2), // Glowing cyan
                        foregroundColor: Colors.black,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        elevation: 10,
                        shadowColor: const Color(0xFF00FFD2).withOpacity(0.5),
                      ),
                      icon: const Icon(Icons.flash_on, size: 16),
                      label: const Text(
                        'Fire Synapse Connection',
                        style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.bold,
                          letterSpacing: 0.5,
                        ),
                      ),
                    ),
                    const SizedBox(height: 24),
                  ],

                  // Section: Label
                  _buildSectionTitle('IDENTIFIER'),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _labelController,
                    onChanged: (val) {
                      widget.state.updateNodeLabel(widget.node.id, val);
                    },
                    style: const TextStyle(color: Colors.white, fontSize: 13),
                    decoration: InputDecoration(
                      hintText: 'e.g. IngressFilter',
                      hintStyle: const TextStyle(color: Colors.white30),
                      fillColor: Colors.white.withValues(alpha: 0.02),
                      filled: true,
                      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.08)),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: const BorderSide(color: Color(0xFFFF9F1C)),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),

                  // Section: Actions / Handlers
                  _buildSectionTitle('HANDLERS'),
                  const SizedBox(height: 8),
                  _buildHandlerRow(
                    trigger: 'incoming synapse',
                    action: widget.node.id == 'llm_translation_neuron' ? 'TranslateTextRequest' : 'TextTranslatedEvent',
                  ),
                  const SizedBox(height: 20),

                  // Section: Lifecycle Reactions
                  _buildSectionTitle('LIFECYCLE REACTIONS'),
                  const SizedBox(height: 8),
                  _buildReactionRow(
                    event: 'when activated',
                    response: 'wire to timeline streams',
                  ),
                  const SizedBox(height: 24),

                  // Section: Expression / Surface Code
                  _buildSectionTitle('RFW SURFACE / SCRIPT'),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _payloadController,
                    maxLines: 6,
                    onChanged: (val) {
                      widget.state.updateNodePayload(widget.node.id, val);
                    },
                    style: const TextStyle(
                      color: Colors.white70,
                      fontSize: 11,
                      fontFamily: 'JetBrains Mono',
                    ),
                    decoration: InputDecoration(
                      fillColor: Colors.black.withValues(alpha: 0.3),
                      filled: true,
                      contentPadding: const EdgeInsets.all(12),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.08)),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: const BorderSide(color: Color(0xFFFF9F1C)),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),

                  // Danger Zone: Delete Node
                  OutlinedButton(
                    onPressed: () {
                      widget.state.removeNode(widget.node.id);
                    },
                    style: OutlinedButton.styleFrom(
                      foregroundColor: const Color(0xFFFF2D55),
                      side: BorderSide(color: const Color(0xFFFF2D55).withValues(alpha: 0.3)),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                      padding: const EdgeInsets.symmetric(vertical: 12),
                    ),
                    child: const Text(
                      'Delete Node',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildSectionTitle(String text) {
    return Text(
      text,
      style: TextStyle(
        color: Colors.white.withValues(alpha: 0.4),
        fontSize: 10,
        fontWeight: FontWeight.w700,
        letterSpacing: 1.0,
      ),
    );
  }

  Widget _buildHandlerRow({required String trigger, required String action}) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.02),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.05)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Row(
            children: [
              const Icon(Icons.bolt, size: 12, color: Color(0xFFFF9F1C)),
              const SizedBox(width: 6),
              Text(
                trigger,
                style: const TextStyle(color: Colors.white70, fontSize: 11),
              ),
            ],
          ),
          Text(
            action,
            style: const TextStyle(color: Colors.white54, fontSize: 11, fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }

  Widget _buildReactionRow({required String event, required String response}) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.02),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.05)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Row(
            children: [
              const Icon(Icons.sync_alt, size: 12, color: Color(0xFF00D2FF)),
              const SizedBox(width: 6),
              Text(
                event,
                style: const TextStyle(color: Colors.white70, fontSize: 11),
              ),
            ],
          ),
          Text(
            response,
            style: const TextStyle(color: Colors.white54, fontSize: 11, fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }
}
