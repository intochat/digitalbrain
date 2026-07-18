import 'dart:convert';
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:grpc/grpc_or_grpcweb.dart';

import 'package:digitalbrain_flutter/features/live/timeline/synapse_ring_buffer.dart';
import 'package:digitalbrain_flutter/features/live/graph/cluster_layout.dart';
import 'package:digitalbrain_flutter/features/live/graph/domain_palette.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';

/// A premium, glassmorphic bottom drawer to inspect, scrubber-replay,
/// and test-fire live synapses crossing the Orleans timeline.
class DebugDrawer extends StatefulWidget {
  const DebugDrawer({
    super.key,
    required this.buffer,
    required this.scrubIndex,
    required this.liveMode,
    required this.onScrub,
    required this.onTapLive,
    required this.gateway,
    required this.activeScope,
  });

  final SynapseRingBuffer buffer;
  final int scrubIndex;
  final bool liveMode;
  final ValueChanged<int> onScrub;
  final VoidCallback onTapLive;
  final DigitalBrainGatewayClient? gateway;
  final String activeScope;

  @override
  State<DebugDrawer> createState() => _DebugDrawerState();
}

class _DebugDrawerState extends State<DebugDrawer> with SingleTickerProviderStateMixin {
  late final AnimationController _animController;
  late double _drawerHeight;
  bool _isExpanded = false;
  GraphEdge? _selectedSynapse;

  static const double _collapsedHeight = 52.0;

  @override
  void initState() {
    super.initState();
    _drawerHeight = _collapsedHeight;
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 250),
    )..addListener(() {
        if (mounted) {
          setState(() {
            _drawerHeight = _animController.value;
          });
        }
      });

    widget.buffer.addListener(_onBufferUpdated);
  }

  @override
  void dispose() {
    widget.buffer.removeListener(_onBufferUpdated);
    _animController.dispose();
    super.dispose();
  }

  void _onBufferUpdated() {
    if (mounted) {
      setState(() {});
    }
  }

  void _toggleExpanded(double maxDrawerHeight) {
    _isExpanded = !_isExpanded;
    final target = _isExpanded ? maxDrawerHeight : _collapsedHeight;
    _animController.stop();
    _animController.value = _drawerHeight;
    _animController.animateTo(target, curve: Curves.easeOutCubic);
  }

  void _snapDrawer(double maxDrawerHeight, double velocity) {
    _animController.stop();
    _animController.value = _drawerHeight;
    if (velocity < -300) {
      // drag up fast
      _isExpanded = true;
      _animController.animateTo(maxDrawerHeight, curve: Curves.easeOutCubic);
    } else if (velocity > 300) {
      // drag down fast
      _isExpanded = false;
      _animController.animateTo(_collapsedHeight, curve: Curves.easeOutCubic);
    } else {
      // snap based on closest state
      final midpoint = (_collapsedHeight + maxDrawerHeight) / 2;
      _isExpanded = _drawerHeight > midpoint;
      final target = _isExpanded ? maxDrawerHeight : _collapsedHeight;
      _animController.animateTo(target, curve: Curves.easeOutCubic);
    }
  }

  bool _isFailureSynapse(String typeName) {
    final lower = typeName.toLowerCase();
    return lower.contains('failure') || lower.contains('error') || lower.contains('exception');
  }

  String _formatTime(DateTime time) {
    final h = time.hour.toString().padLeft(2, '0');
    final m = time.minute.toString().padLeft(2, '0');
    final s = time.second.toString().padLeft(2, '0');
    final ms = time.millisecond.toString().padLeft(3, '0');
    return '$h:$m:$s.$ms';
  }

  String _decodePayload(List<int> bytes) {
    if (bytes.isEmpty) return '(empty)';
    try {
      final decoded = utf8.decode(bytes, allowMalformed: true);
      // Attempt pretty print JSON
      final obj = jsonDecode(decoded);
      return const JsonEncoder.withIndent('  ').convert(obj);
    } catch (_) {
      try {
        return utf8.decode(bytes, allowMalformed: true);
      } catch (_) {
        return '<${bytes.length} bytes>';
      }
    }
  }

  void _showFireSynapseDialog() {
    if (widget.gateway == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('gRPC Gateway Client is not connected.')),
      );
      return;
    }
    showDialog(
      context: context,
      builder: (ctx) => _FireSynapseDialog(
        gateway: widget.gateway!,
        activeScope: widget.activeScope,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final screenHeight = MediaQuery.of(context).size.height;
    final maxDrawerHeight = screenHeight * 0.50;

    return Positioned(
      left: 0,
      right: 0,
      bottom: 0,
      height: _drawerHeight,
      child: ClipRRect(
        borderRadius: const BorderRadius.only(
          topLeft: Radius.circular(24),
          topRight: Radius.circular(24),
        ),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 20, sigmaY: 20),
          child: Container(
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.78),
              borderRadius: const BorderRadius.only(
                topLeft: Radius.circular(24),
                topRight: Radius.circular(24),
              ),
              border: Border(
                top: BorderSide(
                  color: Colors.white.withValues(alpha: 0.12),
                  width: 1.0,
                ),
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // 1. Gesture Header Area
                GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onVerticalDragUpdate: (details) {
                    setState(() {
                      _drawerHeight = (_drawerHeight - details.delta.dy)
                          .clamp(_collapsedHeight, maxDrawerHeight);
                    });
                  },
                  onVerticalDragEnd: (details) {
                    _snapDrawer(maxDrawerHeight, details.primaryVelocity ?? 0.0);
                  },
                  onTap: () => _toggleExpanded(maxDrawerHeight),
                  child: Container(
                    height: _collapsedHeight,
                    padding: const EdgeInsets.symmetric(horizontal: 20),
                    child: Row(
                      children: [
                        // Glassy pull-up indicator line
                        const Icon(
                          Icons.drag_handle,
                          color: Colors.white38,
                          size: 20,
                        ),
                        const SizedBox(width: 12),
                        Text(
                          'ORLEANS TIMELINE DEBUGGER',
                          style: GoogleFonts.outfit(
                            fontSize: 12,
                            fontWeight: FontWeight.bold,
                            letterSpacing: 1.2,
                            color: const Color(0xFFFF9F1C),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                          decoration: BoxDecoration(
                            color: Colors.white.withValues(alpha: 0.06),
                            borderRadius: BorderRadius.circular(12),
                            border: Border.all(
                              color: Colors.white.withValues(alpha: 0.1),
                              width: 0.5,
                            ),
                          ),
                          child: Text(
                            '${widget.buffer.length}/${widget.buffer.capacity} synapses',
                            style: GoogleFonts.jetBrainsMono(
                              fontSize: 10,
                              color: Colors.white60,
                            ),
                          ),
                        ),
                        const Spacer(),
                        // Fire Test Synapse
                        ElevatedButton.icon(
                          onPressed: _showFireSynapseDialog,
                          style: ElevatedButton.styleFrom(
                            backgroundColor: const Color(0xFFFF9F1C).withValues(alpha: 0.2),
                            foregroundColor: const Color(0xFFFF9F1C),
                            side: const BorderSide(color: Color(0xFFFF9F1C), width: 0.5),
                            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(12),
                            ),
                          ),
                          icon: const Icon(Icons.bolt, size: 14),
                          label: Text(
                            'Fire Synapse',
                            style: GoogleFonts.outfit(
                              fontSize: 11,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                        const SizedBox(width: 12),
                        // Collapse/Expand Icon button
                        Icon(
                          _isExpanded ? Icons.keyboard_arrow_down : Icons.keyboard_arrow_up,
                          color: Colors.white54,
                        ),
                      ],
                    ),
                  ),
                ),
                // 2. Scrubber Strip
                _buildScrubberStrip(),
                // 3. Main Split-Pane Inspector Content (Visible only when expanded)
                if (_drawerHeight > _collapsedHeight + 10)
                  Expanded(
                    child: FadeTransition(
                      opacity: AlwaysStoppedAnimation(
                        ((_drawerHeight - _collapsedHeight) / (maxDrawerHeight - _collapsedHeight))
                            .clamp(0.0, 1.0),
                      ),
                      child: Padding(
                        padding: const EdgeInsets.only(left: 20, right: 20, bottom: 20),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            // Left Pane: Ledger
                            Expanded(
                              flex: 3,
                              child: _buildLedger(),
                            ),
                            const SizedBox(width: 16),
                            // Right Pane: JSON payload inspector
                            Expanded(
                              flex: 2,
                              child: _buildPayloadInspector(),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildScrubberStrip() {
    final total = widget.buffer.length;
    final maxIndex = (total == 0 ? 1 : total - 1).toDouble();
    final clampedScrub = total == 0
        ? 0.0
        : widget.scrubIndex.toDouble().clamp(0.0, (total - 1).toDouble());

    return Container(
      height: 40,
      padding: const EdgeInsets.symmetric(horizontal: 20),
      decoration: BoxDecoration(
        border: Border(
          bottom: BorderSide(
            color: Colors.white.withValues(alpha: 0.05),
            width: 1.0,
          ),
        ),
      ),
      child: Row(
        children: [
          // Left step back button
          IconButton(
            icon: const Icon(Icons.chevron_left, size: 18),
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(),
            color: widget.liveMode ? Colors.white30 : Colors.white70,
            onPressed: widget.liveMode || widget.scrubIndex <= 0
                ? null
                : () => widget.onScrub(widget.scrubIndex - 1),
          ),
          const SizedBox(width: 6),
          // Main scrubber slider
          Expanded(
            child: SliderTheme(
              data: Theme.of(context).sliderTheme.copyWith(
                    activeTrackColor: const Color(0xFFFF9F1C),
                    inactiveTrackColor: Colors.white10,
                    trackHeight: 2,
                    thumbColor: widget.liveMode ? const Color(0xFF6EE7A8) : const Color(0xFFFF9F1C),
                    overlayColor: const Color(0xFFFF9F1C).withValues(alpha: 0.12),
                    showValueIndicator: ShowValueIndicator.onDrag,
                  ),
              child: Slider(
                min: 0,
                max: maxIndex,
                value: widget.liveMode ? maxIndex : clampedScrub,
                label: widget.liveMode ? 'live' : '#${widget.scrubIndex}',
                onChanged: total == 0 ? null : (v) => widget.onScrub(v.toInt()),
              ),
            ),
          ),
          const SizedBox(width: 6),
          // Right step forward button
          IconButton(
            icon: const Icon(Icons.chevron_right, size: 18),
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(),
            color: widget.liveMode || widget.scrubIndex >= total - 1 ? Colors.white30 : Colors.white70,
            onPressed: widget.liveMode || widget.scrubIndex >= total - 1
                ? null
                : () => widget.onScrub(widget.scrubIndex + 1),
          ),
          const SizedBox(width: 16),
          // Live toggle button
          ElevatedButton(
            onPressed: widget.liveMode ? null : widget.onTapLive,
            style: ElevatedButton.styleFrom(
              backgroundColor: const Color(0xFF6EE7A8),
              disabledBackgroundColor: const Color(0xFF6EE7A8).withValues(alpha: 0.25),
              foregroundColor: Colors.black,
              disabledForegroundColor: const Color(0xFF6EE7A8),
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
              ),
            ),
            child: Text(
              widget.liveMode ? 'LIVE' : 'FREEZE',
              style: GoogleFonts.outfit(
                fontSize: 10,
                fontWeight: FontWeight.bold,
                letterSpacing: 0.5,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildLedger() {
    final list = widget.buffer.items;
    if (list.isEmpty) {
      return Container(
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.02),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: Colors.white.withValues(alpha: 0.05)),
        ),
        child: const Center(
          child: Text(
            'No synapses tracked yet.',
            style: TextStyle(color: Colors.white38, fontSize: 12),
          ),
        ),
      );
    }

    return Container(
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.01),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: Colors.white.withValues(alpha: 0.06)),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(14),
        child: Column(
          children: [
            // Ledger Header Row
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
              color: Colors.white.withValues(alpha: 0.04),
              child: Row(
                children: [
                  SizedBox(
                    width: 90,
                    child: Text(
                      'TIMESTAMP',
                      style: GoogleFonts.outfit(fontSize: 10, fontWeight: FontWeight.bold, color: Colors.white38),
                    ),
                  ),
                  SizedBox(
                    width: 100,
                    child: Text(
                      'CALLER',
                      style: GoogleFonts.outfit(fontSize: 10, fontWeight: FontWeight.bold, color: Colors.white38),
                    ),
                  ),
                  SizedBox(
                    width: 100,
                    child: Text(
                      'RECEIVER',
                      style: GoogleFonts.outfit(fontSize: 10, fontWeight: FontWeight.bold, color: Colors.white38),
                    ),
                  ),
                  Expanded(
                    child: Text(
                      'SYNAPSE FQN',
                      style: GoogleFonts.outfit(fontSize: 10, fontWeight: FontWeight.bold, color: Colors.white38),
                    ),
                  ),
                ],
              ),
            ),
            // Ledger Scrolling Rows
            Expanded(
              child: ListView.builder(
                padding: EdgeInsets.zero,
                itemCount: list.length,
                itemBuilder: (ctx, index) {
                  // Reverse order for ledger (newest at the top)
                  final itemIndex = list.length - 1 - index;
                  final item = list[itemIndex];
                  final isFailure = _isFailureSynapse(item.typeName);
                  final isSelected = _selectedSynapse?.correlationId == item.correlationId;

                  Color rowColor = Colors.transparent;
                  if (isSelected) {
                    rowColor = const Color(0xFFFF9F1C).withValues(alpha: 0.12);
                  } else if (isFailure) {
                    rowColor = const Color(0xFFFF453A).withValues(alpha: 0.09);
                  } else if (itemIndex == widget.scrubIndex && !widget.liveMode) {
                    rowColor = Colors.white.withValues(alpha: 0.06);
                  }

                  Color fqnColor = isFailure ? const Color(0xFFFF453A) : Colors.white70;
                  if (isSelected) fqnColor = const Color(0xFFFF9F1C);

                  return InkWell(
                    onTap: () {
                      setState(() {
                        _selectedSynapse = item;
                      });
                      // If freezable/scrub-matching, snap scrubber to this item
                      widget.onScrub(itemIndex);
                    },
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                      decoration: BoxDecoration(
                        color: rowColor,
                        border: Border(
                          bottom: BorderSide(
                            color: Colors.white.withValues(alpha: 0.03),
                            width: 0.5,
                          ),
                        ),
                      ),
                      child: Row(
                        children: [
                          // Time Column
                          SizedBox(
                            width: 90,
                            child: Text(
                              _formatTime(item.at),
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 10,
                                color: isSelected ? const Color(0xFFFF9F1C) : Colors.white54,
                              ),
                            ),
                          ),
                          // Caller Column
                          SizedBox(
                            width: 100,
                            child: Text(
                              shortNeuronLabel(item.fromId),
                              overflow: TextOverflow.ellipsis,
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 9.5,
                                color: isSelected ? const Color(0xFFFF9F1C) : Colors.white60,
                              ),
                            ),
                          ),
                          // Receiver Column
                          SizedBox(
                            width: 100,
                            child: Text(
                              shortNeuronLabel(item.toId),
                              overflow: TextOverflow.ellipsis,
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 9.5,
                                color: isSelected ? const Color(0xFFFF9F1C) : Colors.white60,
                              ),
                            ),
                          ),
                          // FQN Column
                          Expanded(
                            child: Text(
                              item.typeName,
                              overflow: TextOverflow.ellipsis,
                              style: GoogleFonts.jetBrainsMono(
                                fontSize: 9.5,
                                color: fqnColor,
                                fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildPayloadInspector() {
    final edge = _selectedSynapse;
    if (edge == null) {
      return Container(
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.01),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: Colors.white.withValues(alpha: 0.05)),
        ),
        child: const Center(
          child: Text(
            'Select a synapse row to inspect its JSON payload.',
            style: TextStyle(color: Colors.white38, fontSize: 11),
          ),
        ),
      );
    }

    final payloadStr = _decodePayload(edge.payload);

    return Container(
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.35),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: Colors.white.withValues(alpha: 0.06)),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Inspector Header
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              color: Colors.white.withValues(alpha: 0.03),
              child: Row(
                children: [
                  Text(
                    'PAYLOAD INSPECTOR',
                    style: GoogleFonts.outfit(
                      fontSize: 9.5,
                      fontWeight: FontWeight.bold,
                      letterSpacing: 0.5,
                      color: Colors.white38,
                    ),
                  ),
                  const Spacer(),
                  // Copy Button
                  IconButton(
                    icon: const Icon(Icons.copy, size: 14),
                    padding: EdgeInsets.zero,
                    constraints: const BoxConstraints(),
                    color: Colors.white60,
                    tooltip: 'Copy payload to clipboard',
                    onPressed: () {
                      Clipboard.setData(ClipboardData(text: payloadStr));
                      ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(
                          content: Row(
                            children: [
                              const Icon(Icons.check_circle, color: Color(0xFF6EE7A8), size: 16),
                              const SizedBox(width: 8),
                              Text(
                                'Payload copied to clipboard!',
                                style: GoogleFonts.outfit(fontSize: 12),
                              ),
                            ],
                          ),
                          backgroundColor: const Color(0xFF101222),
                          behavior: SnackBarBehavior.floating,
                          duration: const Duration(seconds: 2),
                        ),
                      );
                    },
                  ),
                ],
              ),
            ),
            // Inspector Scrollable body
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(14),
                child: SelectionArea(
                  child: Text(
                    payloadStr,
                    style: GoogleFonts.jetBrainsMono(
                      fontSize: 10,
                      color: const Color(0xFF6EE7A8),
                      height: 1.4,
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Dialog containing templates and JSON editor to fire/inject custom synapses into Orleans.
class _FireSynapseDialog extends StatefulWidget {
  const _FireSynapseDialog({
    required this.gateway,
    required this.activeScope,
  });

  final DigitalBrainGatewayClient gateway;
  final String activeScope;

  @override
  State<_FireSynapseDialog> createState() => _FireSynapseDialogState();
}

class _FireSynapseDialogState extends State<_FireSynapseDialog> {
  final _fqnController = TextEditingController();
  final _payloadController = TextEditingController();
  String _selectedTemplate = 'Custom';
  bool _loading = false;
  String? _error;

  static const List<String> _templates = ['Custom', 'LLM Request', 'Accept Policy', 'Voice2Text Request'];

  @override
  void initState() {
    super.initState();
    _applyTemplate('LLM Request');
  }

  void _applyTemplate(String t) {
    setState(() {
      _selectedTemplate = t;
      _error = null;
      switch (t) {
        case 'LLM Request':
          _fqnController.text = 'DigitalBrain.SDK.Ai.LlmRequest';
          _payloadController.text = const JsonEncoder.withIndent('  ').convert({
            'Prompt': 'Describe the core architecture of DigitalBrain in three sentences.',
            'ModelId': 'reasoning',
          });
          break;
        case 'Accept Policy':
          _fqnController.text = 'DigitalBrain.Domains.Onboarding.Contracts.AcceptPolicy';
          _payloadController.text = const JsonEncoder.withIndent('  ').convert({
            'UserId': 'local-user',
            'Version': 'v5.0',
          });
          break;
        case 'Voice2Text Request':
          _fqnController.text = 'DigitalBrain.SDK.Ai.Voice2TextRequest';
          _payloadController.text = const JsonEncoder.withIndent('  ').convert({
            'Audio': [],
            'MimeType': 'audio/wav',
            'LanguageHint': 'en',
            'ReturnSegments': false,
          });
          break;
        case 'Custom':
        default:
          _fqnController.text = '';
          _payloadController.text = '{\n  \n}';
          break;
      }
    });
  }

  Future<void> _inject() async {
    final fqn = _fqnController.text.trim();
    final rawJson = _payloadController.text.trim();

    if (fqn.isEmpty) {
      setState(() => _error = 'Synapse FQN is required.');
      return;
    }

    try {
      jsonDecode(rawJson); // Validate JSON syntax
    } catch (e) {
      setState(() => _error = 'Invalid JSON structure: $e');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final envelope = SynapseEnvelope()
        ..correlationId = ''
        ..typeName = fqn
        ..payload = Uint8List.fromList(utf8.encode(rawJson));

      final options = CallOptions(
        metadata: {
          'x-brain-id': widget.activeScope,
          'x-active-scope': widget.activeScope,
        },
      );

      await widget.gateway.send(envelope, options: options);

      if (mounted) {
        Navigator.of(context).pop();
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Row(
              children: [
                const Icon(Icons.bolt, color: Color(0xFFFF9F1C), size: 18),
                const SizedBox(width: 8),
                Text(
                  'Custom synapse injected successfully into timeline!',
                  style: GoogleFonts.outfit(fontWeight: FontWeight.bold),
                ),
              ],
            ),
            backgroundColor: const Color(0xFF0A0A0C),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Failed to inject: $e';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Dialog(
      backgroundColor: Colors.transparent,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(24),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
          child: Container(
            width: 580,
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.85),
              borderRadius: BorderRadius.circular(24),
              border: Border.all(
                color: Colors.white.withValues(alpha: 0.12),
                width: 1.0,
              ),
            ),
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Header
                Row(
                  children: [
                    const Icon(Icons.bolt, color: Color(0xFFFF9F1C), size: 20),
                    const SizedBox(width: 10),
                    Text(
                      'FIRE TEST SYNAPSE',
                      style: GoogleFonts.outfit(
                        fontSize: 14,
                        fontWeight: FontWeight.bold,
                        letterSpacing: 1.0,
                        color: Colors.white,
                      ),
                    ),
                    const Spacer(),
                    IconButton(
                      icon: const Icon(Icons.close, size: 18, color: Colors.white54),
                      onPressed: () => Navigator.of(context).pop(),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                // Template Selector Dropdown
                Row(
                  children: [
                    Text(
                      'Preset Template: ',
                      style: GoogleFonts.outfit(fontSize: 12, color: Colors.white60),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        decoration: BoxDecoration(
                          color: Colors.white.withValues(alpha: 0.05),
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: Colors.white.withValues(alpha: 0.1), width: 0.5),
                        ),
                        child: DropdownButtonHideUnderline(
                          child: DropdownButton<String>(
                            value: _selectedTemplate,
                            dropdownColor: Colors.black87,
                            iconEnabledColor: Colors.white70,
                            items: _templates.map((String t) {
                              return DropdownMenuItem<String>(
                                value: t,
                                child: Text(
                                  t,
                                  style: GoogleFonts.outfit(fontSize: 12, color: Colors.white),
                                ),
                              );
                            }).toList(),
                            onChanged: (val) {
                              if (val != null) _applyTemplate(val);
                            },
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                // FQN Field
                TextField(
                  controller: _fqnController,
                  style: GoogleFonts.jetBrainsMono(fontSize: 12, color: Colors.white),
                  decoration: InputDecoration(
                    labelText: 'SYNAPSE TYPE FQN',
                    labelStyle: GoogleFonts.outfit(fontSize: 10, color: Colors.white38),
                    filled: true,
                    fillColor: Colors.white.withValues(alpha: 0.04),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.1), width: 0.5),
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: Color(0xFFFF9F1C), width: 1.0),
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                // Payload Editor text area
                Expanded(
                  child: Container(
                    height: 220,
                    decoration: BoxDecoration(
                      color: Colors.black.withValues(alpha: 0.4),
                      borderRadius: BorderRadius.circular(14),
                      border: Border.all(color: Colors.white.withValues(alpha: 0.1), width: 0.5),
                    ),
                    child: TextField(
                      controller: _payloadController,
                      maxLines: null,
                      expands: true,
                      keyboardType: TextInputType.multiline,
                      style: GoogleFonts.jetBrainsMono(fontSize: 11, color: const Color(0xFF6EE7A8)),
                      decoration: const InputDecoration(
                        contentPadding: EdgeInsets.all(14),
                        border: InputBorder.none,
                        hintText: 'Enter JSON payload here...',
                        hintStyle: TextStyle(color: Colors.white24, fontSize: 11),
                      ),
                    ),
                  ),
                ),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    _error!,
                    style: GoogleFonts.outfit(color: const Color(0xFFFF453A), fontSize: 11),
                  ),
                ],
                const SizedBox(height: 18),
                // Action Buttons
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [
                    TextButton(
                      onPressed: _loading ? null : () => Navigator.of(context).pop(),
                      child: Text(
                        'Cancel',
                        style: GoogleFonts.outfit(color: Colors.white54, fontSize: 13),
                      ),
                    ),
                    const SizedBox(width: 14),
                    ElevatedButton(
                      onPressed: _loading ? null : _inject,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: const Color(0xFFFF9F1C),
                        foregroundColor: Colors.black,
                        padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 12),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(99),
                        ),
                      ),
                      child: _loading
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                valueColor: AlwaysStoppedAnimation<Color>(Colors.black),
                              ),
                            )
                          : Text(
                              'Inject Synapse',
                              style: GoogleFonts.outfit(
                                fontSize: 13,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
