import 'dart:async';
import 'package:flutter/material.dart';
import '../../live/introspector_client.dart';
import '../visual_constructor_models.dart';
import '../visual_constructor_state.dart';

class ConstructorFab extends StatefulWidget {
  final VisualConstructorState state;
  final IntrospectorClient? introspector;

  const ConstructorFab({
    super.key,
    required this.state,
    this.introspector,
  });

  @override
  State<ConstructorFab> createState() => _ConstructorFabState();
}

class _ConstructorFabState extends State<ConstructorFab> with SingleTickerProviderStateMixin {
  bool _isOpen = false;
  late AnimationController _controller;
  late Animation<double> _expandAnimation;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      duration: const Duration(milliseconds: 250),
      vsync: this,
    );
    _expandAnimation = CurvedAnimation(
      parent: _controller,
      curve: Curves.easeOutBack,
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _toggleMenu() {
    setState(() {
      _isOpen = !_isOpen;
      if (_isOpen) {
        _controller.forward();
      } else {
        _controller.reverse();
      }
    });
  }

  void _addNewNeuronDialog() {
    _toggleMenu();
    final nameController = TextEditingController();
    String dddShape = 'Domain';

    showDialog(
      context: context,
      builder: (ctx) {
        return StatefulBuilder(
          builder: (context, setState) {
            return AlertDialog(
              title: const Text('New Neuron Factory', style: TextStyle(color: Colors.white, fontSize: 16)),
              content: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextField(
                    controller: nameController,
                    style: const TextStyle(color: Colors.white70),
                    decoration: const InputDecoration(
                      labelText: 'Neuron FQN',
                      labelStyle: TextStyle(color: Colors.white30),
                    ),
                  ),
                  const SizedBox(height: 16),
                  DropdownButton<String>(
                    value: dddShape,
                    dropdownColor: const Color(0xFF0A0A0C),
                    style: const TextStyle(color: Colors.white),
                    isExpanded: true,
                    items: ['Domain', 'Connector', 'Role'].map((String val) {
                      return DropdownMenuItem<String>(
                        value: val,
                        child: Text(val),
                      );
                    }).toList(),
                    onChanged: (newVal) {
                      if (newVal != null) {
                        setState(() {
                          dddShape = newVal;
                        });
                      }
                    },
                  ),
                ],
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(ctx),
                  child: const Text('Cancel'),
                ),
                TextButton(
                  onPressed: () {
                    final fqn = nameController.text.trim();
                    if (fqn.isNotEmpty) {
                      final newNode = VisualNode(
                        id: 'node_${DateTime.now().millisecondsSinceEpoch}',
                        kind: NodeKind.neuron,
                        label: fqn,
                        position: const Offset(200, 200),
                        ports: [
                          NodePort(id: 'in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
                          NodePort(id: 'out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
                        ],
                        codePayload: 'on synapse(DB.User.Request) it:\n  log "Executed $fqn"\n  emit signal(DB.Success)(result: "done")',
                      );
                      widget.state.addNode(newNode);
                    }
                    Navigator.pop(ctx);
                  },
                  child: const Text('Create', style: TextStyle(color: Color(0xFFFF9F1C))),
                ),
              ],
            );
          },
        );
      },
    );
  }

  void _addNewSynapseDialog() {
    _toggleMenu();
    final nameController = TextEditingController();

    showDialog(
      context: context,
      builder: (ctx) {
        return AlertDialog(
          title: const Text('New Synapse Schema', style: TextStyle(color: Colors.white, fontSize: 16)),
          content: TextField(
            controller: nameController,
            style: const TextStyle(color: Colors.white70),
            decoration: const InputDecoration(
              labelText: 'Synapse Name / Schema',
              labelStyle: TextStyle(color: Colors.white30),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('Cancel'),
            ),
            TextButton(
              onPressed: () {
                final name = nameController.text.trim();
                if (name.isNotEmpty) {
                  final newNode = VisualNode(
                    id: 'node_${DateTime.now().millisecondsSinceEpoch}',
                    kind: NodeKind.synapse,
                    label: name,
                    position: const Offset(250, 250),
                    ports: [
                      NodePort(id: 'in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
                      NodePort(id: 'out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
                    ],
                    codePayload: 'let summary = "Payload parsed"',
                  );
                  widget.state.addNode(newNode);
                }
                Navigator.pop(ctx);
              },
              child: const Text('Create', style: TextStyle(color: Color(0xFFFF9F1C))),
            ),
          ],
        );
      },
    );
  }

  void _addReferenceSdkDialog() {
    _toggleMenu();
    final localConnectors = [
      ('OpenAI.ChatGpt', 'AI Reasoning, text generation, and intent classification'),
      ('Google.Gmail', 'Read inbox feeds, send emails, watch folder changes'),
      ('Telegram.Bot', 'Read chat channels, broadcast alerts, manage sessions'),
      ('SQLite.Database', 'Durable local relational database records'),
      ('Stripe.Billing', 'Create subscription invoices and watch webhook payouts'),
      ('Postgres.Db', 'Distributed SQL databases cluster resource'),
    ];

    final searchController = TextEditingController();
    List<(String, String)> filteredLocal = List.from(localConnectors);
    List<NeuronSearchResult> remoteResults = [];
    bool loadingRemote = false;
    Timer? searchDebounce;

    showDialog(
      context: context,
      builder: (ctx) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            void onSearchChanged(String query) {
              final clean = query.trim().toLowerCase();
              setDialogState(() {
                filteredLocal = localConnectors
                    .where((c) =>
                        c.$1.toLowerCase().contains(clean) ||
                        c.$2.toLowerCase().contains(clean))
                    .toList();
              });

              if (widget.introspector == null || clean.isEmpty) {
                setDialogState(() {
                  remoteResults = [];
                  loadingRemote = false;
                });
                return;
              }

              if (searchDebounce?.isActive ?? false) searchDebounce!.cancel();
              setDialogState(() {
                loadingRemote = true;
              });
              searchDebounce = Timer(const Duration(milliseconds: 300), () async {
                try {
                  final results = await widget.introspector!.findNeuronsByFeatureText(query, 5);
                  setDialogState(() {
                    remoteResults = results;
                    loadingRemote = false;
                  });
                } catch (_) {
                  setDialogState(() {
                    loadingRemote = false;
                  });
                }
              });
            }

            return AlertDialog(
              backgroundColor: const Color(0xFF0C0C0E),
              title: const Text('Reference SDK Catalog', style: TextStyle(color: Colors.white, fontSize: 16)),
              content: SizedBox(
                width: 440,
                height: 400,
                child: Column(
                  children: [
                    TextField(
                      controller: searchController,
                      style: const TextStyle(color: Colors.white, fontSize: 13),
                      onChanged: onSearchChanged,
                      decoration: InputDecoration(
                        hintText: 'Search connectors and Orleans grains...',
                        hintStyle: const TextStyle(color: Colors.white30, fontSize: 12),
                        prefixIcon: const Icon(Icons.search, color: Colors.white30, size: 16),
                        isDense: true,
                        filled: true,
                        fillColor: Colors.white.withValues(alpha: 0.03),
                        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                        border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(8),
                          borderSide: const BorderSide(color: Colors.white12),
                        ),
                        enabledBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(8),
                          borderSide: BorderSide(color: Colors.white.withValues(alpha: 0.05)),
                        ),
                        focusedBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(8),
                          borderSide: BorderSide(color: const Color(0xFFFF9F1C).withValues(alpha: 0.5)),
                        ),
                        suffixIcon: loadingRemote
                            ? const Padding(
                                padding: EdgeInsets.all(10),
                                child: SizedBox(
                                  width: 12,
                                  height: 12,
                                  child: CircularProgressIndicator(strokeWidth: 1.5, valueColor: AlwaysStoppedAnimation<Color>(Color(0xFFFF9F1C))),
                                ),
                              )
                            : null,
                      ),
                    ),
                    const SizedBox(height: 14),
                    Expanded(
                      child: ListView(
                        children: [
                          if (filteredLocal.isNotEmpty) ...[
                            const Padding(
                              padding: EdgeInsets.only(bottom: 6, top: 4),
                              child: Text(
                                'LOCAL CONNECTORS',
                                style: TextStyle(color: Colors.white30, fontSize: 9, fontWeight: FontWeight.bold, letterSpacing: 0.5),
                              ),
                            ),
                            for (final c in filteredLocal)
                              ListTile(
                                contentPadding: EdgeInsets.zero,
                                title: Text(
                                  c.$1,
                                  style: const TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w600),
                                ),
                                subtitle: Text(
                                  c.$2,
                                  style: const TextStyle(color: Colors.white38, fontSize: 10),
                                ),
                                trailing: const Icon(Icons.add, size: 16, color: Color(0xFFFF9F1C)),
                                onTap: () {
                                  final newNode = VisualNode(
                                    id: 'node_${DateTime.now().millisecondsSinceEpoch}',
                                    kind: NodeKind.neuron,
                                    label: 'DigitalBrain.SDK.${c.$1}',
                                    position: const Offset(200, 180),
                                    ports: [
                                      NodePort(id: 'in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
                                      NodePort(id: 'out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
                                    ],
                                    codePayload: 'log "Activated SDK connector for ${c.$1}"',
                                  );
                                  widget.state.addNode(newNode);
                                  Navigator.pop(ctx);
                                },
                              ),
                          ],
                          if (remoteResults.isNotEmpty) ...[
                            const Padding(
                              padding: EdgeInsets.only(bottom: 6, top: 12),
                              child: Text(
                                'SEMANTIC CORTEX MATCHES',
                                style: TextStyle(color: Color(0xFFFF9F1C), fontSize: 9, fontWeight: FontWeight.bold, letterSpacing: 0.5),
                              ),
                            ),
                            for (final r in remoteResults)
                              ListTile(
                                contentPadding: EdgeInsets.zero,
                                title: Text(
                                  r.neuronType.split('.').last,
                                  style: const TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w600),
                                ),
                                subtitle: Text(
                                  r.neuronType,
                                  style: const TextStyle(color: Colors.white38, fontSize: 10),
                                ),
                                trailing: const Icon(Icons.link_rounded, size: 16, color: Color(0xFFFF9F1C)),
                                onTap: () {
                                  final newNode = VisualNode(
                                    id: 'node_${DateTime.now().millisecondsSinceEpoch}',
                                    kind: NodeKind.neuron,
                                    label: r.neuronType,
                                    referencedNeuronFqn: r.neuronType,
                                    position: const Offset(220, 200),
                                    ports: [
                                      NodePort(id: 'in', name: 'In', isInput: true, relativeOffset: const Offset(0, 50)),
                                      NodePort(id: 'out', name: 'Out', isInput: false, relativeOffset: const Offset(180, 50)),
                                    ],
                                    codePayload: 'log "Referenced grain ${r.neuronType}"',
                                  );
                                  widget.state.addNode(newNode);
                                  Navigator.pop(ctx);
                                },
                              ),
                          ],
                          if (filteredLocal.isEmpty && remoteResults.isEmpty && !loadingRemote)
                            const Padding(
                              padding: EdgeInsets.symmetric(vertical: 32),
                              child: Center(
                                child: Text(
                                  'No catalog or active cortex matching found.',
                                  style: TextStyle(color: Colors.white24, fontSize: 11),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(ctx),
                  child: const Text('Close'),
                ),
              ],
            );
          },
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        // Expanded Options
        SizeTransition(
          sizeFactor: _expandAnimation,
          child: ScaleTransition(
            scale: _expandAnimation,
            child: Padding(
              padding: const EdgeInsets.only(bottom: 12.0),
              child: Column(
                children: [
                  _buildSubFab(
                    label: 'SDK Reference',
                    icon: Icons.api,
                    color: const Color(0xFF00D2FF),
                    onTap: _addReferenceSdkDialog,
                  ),
                  const SizedBox(height: 8),
                  _buildSubFab(
                    label: 'New Synapse',
                    icon: Icons.grain,
                    color: const Color(0xFF5856D6),
                    onTap: _addNewSynapseDialog,
                  ),
                  const SizedBox(height: 8),
                  _buildSubFab(
                    label: 'New Neuron',
                    icon: Icons.psychology,
                    color: const Color(0xFFFF9F1C),
                    onTap: _addNewNeuronDialog,
                  ),
                ],
              ),
            ),
          ),
        ),

        // Main FAB Circular Button
        GestureDetector(
          onTap: _toggleMenu,
          child: Container(
            width: 56,
            height: 56,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: _isOpen
                    ? [
                        Colors.white.withValues(alpha: 0.15),
                        Colors.white.withValues(alpha: 0.05),
                      ]
                    : [
                        const Color(0xFFFF9F1C),
                        const Color(0xFFFF9500),
                      ],
              ),
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: _isOpen
                      ? Colors.black.withValues(alpha: 0.3)
                      : const Color(0xFFFF9F1C).withValues(alpha: 0.4),
                  blurRadius: 15,
                  spreadRadius: 1,
                ),
              ],
              border: Border.all(
                color: Colors.white.withValues(alpha: 0.15),
                width: 1.0,
              ),
            ),
            child: AnimatedRotation(
              duration: const Duration(milliseconds: 200),
              turns: _isOpen ? 0.125 : 0.0, // Rotate 45 deg to look like x
              child: Icon(
                Icons.add,
                color: _isOpen ? Colors.white : Colors.black,
                size: 26,
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildSubFab({
    required String label,
    required IconData icon,
    required Color color,
    required VoidCallback onTap,
  }) {
    return GestureDetector(
      onTap: onTap,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            margin: const EdgeInsets.only(right: 8),
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.7),
              borderRadius: BorderRadius.circular(6),
              border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
            ),
            child: Text(
              label,
              style: const TextStyle(
                color: Colors.white70,
                fontSize: 10,
                fontWeight: FontWeight.w600,
                letterSpacing: 0.5,
              ),
            ),
          ),
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.8),
              shape: BoxShape.circle,
              border: Border.all(color: color.withValues(alpha: 0.5), width: 1.5),
              boxShadow: [
                BoxShadow(
                  color: Colors.black26,
                  blurRadius: 8,
                ),
              ],
            ),
            child: Icon(
              icon,
              color: color,
              size: 18,
            ),
          ),
        ],
      ),
    );
  }
}
