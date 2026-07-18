import 'dart:async';
import 'dart:math';
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import 'package:digitalbrain_flutter/features/live/introspector_client.dart';
import 'package:digitalbrain_flutter/features/live/graph/domain_palette.dart';

class CommandPaletteOverlay extends StatefulWidget {
  final IntrospectorClient introspector;
  final VoidCallback onClose;
  final ValueChanged<String> onSelectNeuron;

  const CommandPaletteOverlay({
    super.key,
    required this.introspector,
    required this.onClose,
    required this.onSelectNeuron,
  });

  @override
  State<CommandPaletteOverlay> createState() => _CommandPaletteOverlayState();
}

class _CommandPaletteOverlayState extends State<CommandPaletteOverlay>
    with SingleTickerProviderStateMixin {
  final _controller = TextEditingController();
  final _focusNode = FocusNode();
  Timer? _debounceTimer;

  bool _loading = false;
  String? _error;
  List<NeuronSearchResult> _results = [];

  late AnimationController _animController;
  late Animation<double> _scaleAnimation;
  late Animation<double> _fadeAnimation;

  @override
  void initState() {
    super.initState();
    _focusNode.requestFocus();
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 200),
    );
    _scaleAnimation = Tween<double>(begin: 0.95, end: 1.0).animate(
      CurvedAnimation(parent: _animController, curve: Curves.easeOutCubic),
    );
    _fadeAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animController, curve: Curves.easeIn),
    );
    _animController.forward();
  }

  @override
  void dispose() {
    _controller.dispose();
    _focusNode.dispose();
    _debounceTimer?.cancel();
    _animController.dispose();
    super.dispose();
  }

  void _onChanged(String query) {
    if (_debounceTimer?.isActive ?? false) _debounceTimer!.cancel();
    _debounceTimer = Timer(const Duration(milliseconds: 250), () {
      _performSearch(query);
    });
  }

  Future<void> _performSearch(String query) async {
    final cleanQuery = query.trim();
    if (cleanQuery.isEmpty) {
      setState(() {
        _results = [];
        _loading = false;
        _error = null;
      });
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await widget.introspector.findNeuronsByFeatureText(cleanQuery, 10);
      if (mounted) {
        setState(() {
          _results = results;
          _loading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = 'Semantic index offline. Try running target nodes first.';
          _loading = false;
        });
      }
    }
  }

  Future<void> _closeWithAnim() async {
    await _animController.reverse();
    widget.onClose();
  }

  @override
  Widget build(BuildContext context) {
    final size = MediaQuery.of(context).size;

    return Stack(
      children: [
        // Backdrop Blur overlay
        Positioned.fill(
          child: GestureDetector(
            onTap: _closeWithAnim,
            behavior: HitTestBehavior.opaque,
            child: ClipRect(
              child: BackdropFilter(
                filter: ImageFilter.blur(sigmaX: 16.0, sigmaY: 16.0),
                child: Container(
                  color: Colors.black.withValues(alpha: 0.55),
                ),
              ),
            ),
          ),
        ),

        // Centered Command Palette
        Center(
          child: SlideTransition(
            position: Tween<Offset>(
              begin: const Offset(0.0, 0.05),
              end: Offset.zero,
            ).animate(CurvedAnimation(parent: _animController, curve: Curves.easeOutCubic)),
            child: ScaleTransition(
              scale: _scaleAnimation,
              child: FadeTransition(
                opacity: _fadeAnimation,
                child: Container(
                  width: min(650, size.width - 48),
                  height: 440,
                  decoration: BoxDecoration(
                    color: const Color(0xFF0C0C0E).withValues(alpha: 0.85),
                    borderRadius: BorderRadius.circular(24),
                    border: Border.all(
                      color: Colors.white.withValues(alpha: 0.08),
                      width: 1.5,
                    ),
                    boxShadow: [
                      BoxShadow(
                        color: Colors.black.withValues(alpha: 0.55),
                        blurRadius: 40,
                        spreadRadius: 5,
                        offset: const Offset(0, 20),
                      ),
                      BoxShadow(
                        color: const Color(0xFFFF9F1C).withValues(alpha: 0.05),
                        blurRadius: 30,
                        spreadRadius: 1,
                      ),
                    ],
                  ),
                  child: Column(
                    children: [
                      // Search Input Header
                      Padding(
                        padding: const EdgeInsets.fromLTRB(20, 20, 20, 16),
                        child: Row(
                          children: [
                            const Icon(
                              Icons.psychology,
                              color: Color(0xFFFF9F1C),
                              size: 24,
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: TextField(
                                controller: _controller,
                                focusNode: _focusNode,
                                onChanged: _onChanged,
                                style: GoogleFonts.outfit(
                                  color: Colors.white,
                                  fontSize: 16,
                                  fontWeight: FontWeight.w500,
                                ),
                                decoration: InputDecoration(
                                  hintText: 'Semantic vector search over cortex…',
                                  hintStyle: GoogleFonts.outfit(
                                    color: Colors.white30,
                                    fontSize: 15,
                                    fontWeight: FontWeight.w400,
                                  ),
                                  border: InputBorder.none,
                                  isDense: true,
                                  contentPadding: EdgeInsets.zero,
                                ),
                              ),
                            ),
                            if (_loading)
                              const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  valueColor: AlwaysStoppedAnimation<Color>(
                                    Color(0xFFFF9F1C),
                                  ),
                                ),
                              )
                            else if (_controller.text.isNotEmpty)
                              IconButton(
                                iconSize: 18,
                                padding: EdgeInsets.zero,
                                constraints: const BoxConstraints(),
                                icon: const Icon(Icons.close, color: Colors.white30),
                                onPressed: () {
                                  _controller.clear();
                                  _onChanged('');
                                },
                              )
                            else
                              Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 6,
                                  vertical: 2,
                                ),
                                decoration: BoxDecoration(
                                  color: Colors.white.withValues(alpha: 0.05),
                                  borderRadius: BorderRadius.circular(4),
                                  border: Border.all(
                                    color: Colors.white.withValues(alpha: 0.08),
                                  ),
                                ),
                                child: const Text(
                                  'ESC',
                                  style: TextStyle(
                                    color: Colors.white30,
                                    fontSize: 8,
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ),
                      const Divider(
                        color: Colors.white10,
                        height: 1,
                      ),

                      // Results Listing
                      Expanded(
                        child: _buildResultsSection(),
                      ),

                      // Command footer hint bar
                      const Divider(
                        color: Colors.white10,
                        height: 1,
                      ),
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Text(
                              'Search matches domains, templates, or scenarios dynamically',
                              style: GoogleFonts.inter(
                                color: Colors.white24,
                                fontSize: 9.5,
                              ),
                            ),
                            Row(
                              children: [
                                const Icon(Icons.keyboard_arrow_up, size: 12, color: Colors.white30),
                                const Icon(Icons.keyboard_arrow_down, size: 12, color: Colors.white30),
                                const SizedBox(width: 4),
                                Text(
                                  'Navigate',
                                  style: GoogleFonts.inter(color: Colors.white30, fontSize: 9.5),
                                ),
                                const SizedBox(width: 14),
                                const Icon(Icons.keyboard_return, size: 10, color: Colors.white30),
                                const SizedBox(width: 4),
                                Text(
                                  'Select',
                                  style: GoogleFonts.inter(color: Colors.white30, fontSize: 9.5),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildResultsSection() {
    if (_error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24.0),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.warning_amber_rounded, color: Color(0xFFFF2D55), size: 28),
              const SizedBox(height: 10),
              Text(
                _error!,
                textAlign: TextAlign.center,
                style: GoogleFonts.inter(
                  color: Colors.white60,
                  fontSize: 12,
                ),
              ),
            ],
          ),
        ),
      );
    }

    if (_controller.text.trim().isEmpty) {
      return _buildTipsView();
    }

    if (_results.isEmpty && !_loading) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.search_off_rounded, color: Colors.white24, size: 36),
            const SizedBox(height: 12),
            Text(
              'No matching neurons or templates found.',
              style: GoogleFonts.inter(
                color: Colors.white30,
                fontSize: 12.5,
              ),
            ),
          ],
        ),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
      itemCount: _results.length,
      separatorBuilder: (_, __) => const SizedBox(height: 6),
      itemBuilder: (ctx, idx) {
        final r = _results[idx];
        final style = styleForDomain(r.domain);
        final label = shortNeuronLabel(r.neuronType);

        return ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: () {
                widget.onSelectNeuron(r.neuronType);
                _closeWithAnim();
              },
              hoverColor: Colors.white.withValues(alpha: 0.04),
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: Colors.white.withValues(alpha: 0.03),
                  ),
                ),
                child: Row(
                  children: [
                    CircleAvatar(
                      radius: 6,
                      backgroundColor: style.color.withValues(alpha: 0.8),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Text(
                                label,
                                style: GoogleFonts.outfit(
                                  color: Colors.white,
                                  fontSize: 13.5,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 6,
                                  vertical: 1.5,
                                ),
                                decoration: BoxDecoration(
                                  color: style.color.withValues(alpha: 0.12),
                                  borderRadius: BorderRadius.circular(4),
                                  border: Border.all(
                                    color: style.color.withValues(alpha: 0.25),
                                  ),
                                ),
                                child: Text(
                                  r.domain.toUpperCase(),
                                  style: GoogleFonts.inter(
                                    color: style.color,
                                    fontSize: 8,
                                    fontWeight: FontWeight.bold,
                                    letterSpacing: 0.5,
                                  ),
                                ),
                              ),
                            ],
                          ),
                          if (r.featureSnippet != null && r.featureSnippet!.isNotEmpty) ...[
                            const SizedBox(height: 4),
                            Text(
                              r.featureSnippet!,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: GoogleFonts.inter(
                                color: Colors.white38,
                                fontSize: 10.5,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                    const Icon(
                      Icons.arrow_forward_ios_rounded,
                      color: Colors.white24,
                      size: 12,
                    ),
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }

  Widget _buildTipsView() {
    final suggestedSearch = [
      ('Gmail', 'Google email feeds, authorization, and notifications', Icons.mail_outline),
      ('Ai', 'Large language models reasoning and classification', Icons.psychology_outlined),
      ('Sqlite', 'Durable local relational database records and cataloging', Icons.storage_outlined),
      ('identity', 'User authentication, security roles and login checks', Icons.fingerprint),
    ];

    return Padding(
      padding: const EdgeInsets.all(20.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'SUGGESTED INQUIRIES',
            style: GoogleFonts.inter(
              color: Colors.white30,
              fontSize: 9.5,
              fontWeight: FontWeight.bold,
              letterSpacing: 1.0,
            ),
          ),
          const SizedBox(height: 12),
          Expanded(
            child: ListView.separated(
              itemCount: suggestedSearch.length,
              separatorBuilder: (_, __) => const SizedBox(height: 8),
              itemBuilder: (ctx, i) {
                final item = suggestedSearch[i];
                return ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: Material(
                    color: Colors.transparent,
                    child: InkWell(
                      onTap: () {
                        _controller.text = item.$1;
                        _performSearch(item.$1);
                      },
                      hoverColor: Colors.white.withValues(alpha: 0.03),
                      child: Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: Colors.white.withValues(alpha: 0.02),
                          ),
                        ),
                        child: Row(
                          children: [
                            Icon(item.$3, color: const Color(0xFFFF9F1C), size: 16),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    item.$1,
                                    style: GoogleFonts.outfit(
                                      color: Colors.white.withValues(alpha: 0.9),
                                      fontSize: 13,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    item.$2,
                                    style: GoogleFonts.inter(
                                      color: Colors.white30,
                                      fontSize: 10,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
