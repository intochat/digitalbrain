import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'pcm_audio_output.dart';
import 'personaplex_voice_controller.dart';

final class PersonaPlexVoiceScreen extends StatefulWidget {
  const PersonaPlexVoiceScreen({
    super.key,
    required this.active,
    this.baseUri,
    this.controllerFactory,
  });

  final bool active;
  final Uri? baseUri;
  final PersonaPlexVoiceControllerFactory? controllerFactory;

  @override
  State<PersonaPlexVoiceScreen> createState() => _PersonaPlexVoiceScreenState();
}

final class _PersonaPlexVoiceScreenState extends State<PersonaPlexVoiceScreen> {
  PersonaPlexVoiceController? _controller;
  Future<void>? _releaseFuture;

  PersonaPlexVoiceControllerFactory? get _factory {
    final injected = widget.controllerFactory;
    if (injected != null) {
      return injected;
    }
    final baseUri = widget.baseUri;
    if (baseUri == null) {
      return null;
    }
    return () => PersonaPlexVoiceController(
      capture: RecordPersonaPlexAudioCapture(),
      output: SoLoudPcmAudioOutput(),
      transport: PersonaPlexVoiceClient(baseUri: baseUri),
    );
  }

  @override
  void didUpdateWidget(covariant PersonaPlexVoiceScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.active && !widget.active) {
      unawaited(_releaseController());
    }
    if (oldWidget.baseUri != widget.baseUri ||
        oldWidget.controllerFactory != widget.controllerFactory) {
      unawaited(_releaseController());
    }
  }

  Future<void> _start() async {
    if (!widget.active) {
      return;
    }
    await _releaseController();
    final factory = _factory;
    if (factory == null || !mounted) {
      return;
    }
    final controller = factory();
    _controller = controller..addListener(_onControllerChanged);
    setState(() {});
    await controller.start();
  }

  void _onControllerChanged() {
    if (mounted) {
      setState(() {});
    }
  }

  Future<void> _releaseController() {
    return _releaseFuture ??= _releaseCurrent().whenComplete(() {
      _releaseFuture = null;
    });
  }

  Future<void> _releaseCurrent() async {
    final controller = _controller;
    if (controller == null) {
      return;
    }
    _controller = null;
    controller.removeListener(_onControllerChanged);
    await controller.disposeAsync();
    controller.dispose();
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void dispose() {
    final controller = _controller;
    _controller = null;
    if (controller != null) {
      controller.removeListener(_onControllerChanged);
      unawaited(controller.disposeAsync());
      controller.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    final available = _factory != null;
    final phase =
        controller?.phase ??
        (available
            ? PersonaPlexVoicePhase.idle
            : PersonaPlexVoicePhase.unavailable);
    final status =
        controller?.statusMessage ??
        (available
            ? 'Ready to start native PersonaPlex voice.'
            : 'Native PersonaPlex voice is unavailable because no Kernel '
                  'endpoint is configured.');
    final running =
        phase == PersonaPlexVoicePhase.connecting ||
        phase == PersonaPlexVoicePhase.active;

    return ColoredBox(
      key: const Key('personaplex_voice_screen'),
      color: BrainPalette.surface,
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(32),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 720),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Native PersonaPlex Voice',
                  style: BrainType.heading,
                ),
                const SizedBox(height: 8),
                Text(
                  'Direct 24 kHz voice-to-voice. This route does not use chat, '
                  'transcription, or external speech services.',
                  style: BrainType.bodyMuted,
                ),
                const SizedBox(height: 28),
                _StatusCard(phase: phase, message: status),
                const SizedBox(height: 20),
                _LevelMeter(
                  key: const Key('personaplex_microphone_level'),
                  label: 'Microphone',
                  icon: Icons.mic_outlined,
                  value: controller?.microphoneLevel ?? 0,
                ),
                const SizedBox(height: 14),
                _LevelMeter(
                  key: const Key('personaplex_speaker_level'),
                  label: 'Speaker',
                  icon: Icons.volume_up_outlined,
                  value: controller?.speakerLevel ?? 0,
                ),
                const SizedBox(height: 20),
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        'Model readiness: ${_phaseLabel(phase)}',
                        style: BrainType.metaStrong,
                      ),
                    ),
                    Text(
                      'Latency: ${controller?.latencyMilliseconds ?? '--'} ms',
                      style: BrainType.meta,
                    ),
                  ],
                ),
                const SizedBox(height: 28),
                FilledButton.icon(
                  key: Key(
                    running
                        ? 'personaplex_voice_stop'
                        : 'personaplex_voice_start',
                  ),
                  onPressed: !available
                      ? null
                      : running
                      ? () => unawaited(_releaseController())
                      : () => unawaited(_start()),
                  icon: Icon(running ? Icons.stop : Icons.graphic_eq),
                  label: Text(running ? 'Stop voice' : 'Start voice'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  static String _phaseLabel(PersonaPlexVoicePhase phase) => switch (phase) {
    PersonaPlexVoicePhase.idle => 'idle',
    PersonaPlexVoicePhase.connecting => 'loading',
    PersonaPlexVoicePhase.active => 'ready',
    PersonaPlexVoicePhase.permissionDenied => 'permission required',
    PersonaPlexVoicePhase.unavailable => 'unavailable',
    PersonaPlexVoicePhase.error => 'error',
    PersonaPlexVoicePhase.stopped => 'stopped',
  };
}

final class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.phase, required this.message});

  final PersonaPlexVoicePhase phase;
  final String message;

  @override
  Widget build(BuildContext context) {
    final color = switch (phase) {
      PersonaPlexVoicePhase.active => BrainPalette.success,
      PersonaPlexVoicePhase.permissionDenied ||
      PersonaPlexVoicePhase.unavailable ||
      PersonaPlexVoicePhase.error => BrainPalette.signal,
      _ => BrainPalette.textMuted,
    };
    final icon = switch (phase) {
      PersonaPlexVoicePhase.active => Icons.check_circle_outline,
      PersonaPlexVoicePhase.permissionDenied => Icons.mic_off_outlined,
      PersonaPlexVoicePhase.unavailable => Icons.block_outlined,
      PersonaPlexVoicePhase.error => Icons.error_outline,
      PersonaPlexVoicePhase.connecting => Icons.sync,
      _ => Icons.info_outline,
    };
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: color.withValues(alpha: 0.45)),
      ),
      child: Row(
        children: [
          Icon(icon, color: color),
          const SizedBox(width: 12),
          Expanded(child: Text(message, style: BrainType.bodyMuted)),
        ],
      ),
    );
  }
}

final class _LevelMeter extends StatelessWidget {
  const _LevelMeter({
    super.key,
    required this.label,
    required this.icon,
    required this.value,
  });

  final String label;
  final IconData icon;
  final double value;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, size: 20, color: BrainPalette.textMuted),
        const SizedBox(width: 10),
        SizedBox(width: 90, child: Text(label, style: BrainType.meta)),
        Expanded(
          child: LinearProgressIndicator(
            value: value.clamp(0.0, 1.0),
            minHeight: 8,
            borderRadius: BorderRadius.circular(6),
            color: BrainPalette.signal,
            backgroundColor: BrainPalette.lineStrong,
          ),
        ),
      ],
    );
  }
}
