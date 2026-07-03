import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';

// Modular VoiceInput widget styled for a premium glassmorphic bottom bar.
class VoiceInput extends StatefulWidget {
  const VoiceInput({
    super.key,
    required this.client,
    required this.onTranscript,
    this.onError,
    this.onListeningChanged,
  });

  final DigitalBrainGatewayClient client;
  final ValueChanged<String> onTranscript;
  final ValueChanged<String>? onError;
  final ValueChanged<bool>? onListeningChanged;

  @override
  State<VoiceInput> createState() => _VoiceInputState();
}

class _VoiceInputState extends State<VoiceInput> with SingleTickerProviderStateMixin {
  final AudioRecorder _recorder = AudioRecorder();
  bool _recording = false;
  bool _busy = false;
  String? _activePath;
  late AnimationController _pulseController;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1000),
    );
  }

  @override
  void dispose() {
    _recorder.dispose();
    _pulseController.dispose();
    super.dispose();
  }

  Future<void> _toggle() async {
    if (_busy) return;
    if (_recording) {
      await _stop();
    } else {
      await _start();
    }
  }

  Future<void> _start() async {
    if (!await _recorder.hasPermission()) {
      widget.onError?.call('Microphone permission denied.');
      return;
    }
    final dir = await getTemporaryDirectory();
    final path =
        '${dir.path}/digitalbrain-${DateTime.now().millisecondsSinceEpoch}.wav';
    await _recorder.start(
      const RecordConfig(
        encoder: AudioEncoder.wav,
        sampleRate: 16000,
        numChannels: 1,
      ),
      path: path,
    );
    setState(() {
      _recording = true;
      _activePath = path;
    });
    widget.onListeningChanged?.call(true);
    _pulseController.repeat(reverse: true);
  }

  Future<void> _stop() async {
    _pulseController.stop();
    final stoppedPath = await _recorder.stop();
    setState(() {
      _recording = false;
      _busy = true;
    });
    widget.onListeningChanged?.call(false);

    final path = stoppedPath ?? _activePath;
    _activePath = null;

    if (path == null || !File(path).existsSync()) {
      setState(() => _busy = false);
      widget.onError?.call('No audio captured.');
      return;
    }

    try {
      final bytes = await File(path).readAsBytes();
      final transcript = await _transcribe(bytes);
      if (transcript.isNotEmpty) widget.onTranscript(transcript);
    } catch (e) {
      widget.onError?.call('Transcribe failed: $e');
    } finally {
      if (mounted) setState(() => _busy = false);
      // Tidy up the temp recording. Errors deleting are non-fatal.
      try { await File(path).delete(); } catch (_) {}
    }
  }

  Future<String> _transcribe(Uint8List bytes) async {
    final controller = StreamController<TranscribeRequest>();
    final future = widget.client.transcribe(controller.stream);

    const chunkSize = 16 * 1024;
    var firstChunk = true;
    for (var offset = 0; offset < bytes.length; offset += chunkSize) {
      final end = (offset + chunkSize).clamp(0, bytes.length);
      final slice = bytes.sublist(offset, end);
      final msg = TranscribeRequest(audioChunk: slice);
      if (firstChunk) {
        msg.mimeType = 'audio/wav';
        firstChunk = false;
      }
      controller.add(msg);
    }
    if (firstChunk) {
      controller.add(TranscribeRequest(mimeType: 'audio/wav'));
    }
    await controller.close();
    final response = await future;
    return response.transcript;
  }

  @override
  Widget build(BuildContext context) {
    if (_busy) {
      return const SizedBox(
        width: 40,
        height: 40,
        child: Padding(
          padding: EdgeInsets.all(10),
          child: CircularProgressIndicator(
            strokeWidth: 2,
            valueColor: AlwaysStoppedAnimation<Color>(Colors.white70),
          ),
        ),
      );
    }

    return AnimatedBuilder(
      animation: _pulseController,
      builder: (context, child) {
        final scale = 1.0 + (_pulseController.value * 0.15);
        final glowColor = _recording
            ? Colors.redAccent.withValues(alpha: 0.3 * (1.0 - _pulseController.value))
            : Colors.transparent;

        return Transform.scale(
          scale: _recording ? scale : 1.0,
          child: Stack(
            alignment: Alignment.center,
            children: [
              if (_recording)
                Positioned.fill(
                  child: CustomPaint(
                    painter: LiquidWavePainter(
                      animationValue: _pulseController.value,
                      color: Colors.redAccent,
                    ),
                  ),
                ),
              Container(
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  boxShadow: [
                    if (_recording)
                      BoxShadow(
                        color: glowColor,
                        blurRadius: 10,
                        spreadRadius: 4,
                      ),
                  ],
                ),
                child: Material(
                  color: _recording ? Colors.redAccent.withValues(alpha: 0.15) : Colors.white.withValues(alpha: 0.05),
                  type: MaterialType.circle,
                  clipBehavior: Clip.antiAlias,
                  child: IconButton(
                    tooltip: _recording ? 'Tap to stop & transcribe' : 'Hold to speak',
                    onPressed: _toggle,
                    icon: Icon(
                      _recording ? Icons.stop : Icons.mic,
                      color: _recording ? Colors.redAccent : Colors.white70,
                    ),
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class LiquidWavePainter extends CustomPainter {
  final double animationValue;
  final Color color;

  LiquidWavePainter({required this.animationValue, required this.color});

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5;

    final width = size.width;
    final height = size.height;
    final midY = height / 2;

    // Draw 3 layers of harmonic waves with phase shifts
    for (int i = 0; i < 3; i++) {
      paint.color = color.withValues(alpha: 0.10 + (i * 0.10));
      final path = Path();
      
      final amplitude = (6.0 + i * 4.0) * (0.3 + 0.7 * math.sin(animationValue * math.pi * 2 + i));
      final frequency = 0.04 + i * 0.02;
      final phase = animationValue * math.pi * 2 + (i * math.pi / 3);

      for (double x = 0; x <= width; x += 1) {
        final y = midY + amplitude * math.sin(x * frequency + phase);
        if (x == 0) {
          path.moveTo(x, y);
        } else {
          path.lineTo(x, y);
        }
      }
      canvas.drawPath(path, paint);
    }
  }

  @override
  bool shouldRepaint(covariant LiquidWavePainter oldDelegate) {
    return oldDelegate.animationValue != animationValue || oldDelegate.color != color;
  }
}
