import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'brain_theme.dart';

final class BrainScreen extends StatelessWidget {
  const BrainScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.statusMessage,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    final connected = statusMessage == null || statusMessage!.isEmpty;
    final lastSequence = turns.isEmpty ? '—' : '${turns.last.sequence}';

    return ColoredBox(
      key: const Key('brain_screen'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 980),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 32),
            children: [
              const Text('Brain', style: BrainType.heading),
              const SizedBox(height: 8),
              const Text(
                'What this DigitalBrain can do, and what remains under your control.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 26),
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: [
                  _MetricCard(
                    label: 'Runtime',
                    value: connected ? 'Connected' : 'Offline',
                    accent: connected
                        ? BrainPalette.success
                        : BrainPalette.signal,
                  ),
                  _MetricCard(label: 'Conversation', value: chatName),
                  _MetricCard(label: 'Last sequence', value: lastSequence),
                ],
              ),
              const SizedBox(height: 30),
              const _SectionLabel('CAPABILITIES'),
              const SizedBox(height: 12),
              const _CapabilityCard(
                icon: Icons.chat_bubble_outline_rounded,
                title: 'General assistant',
                body:
                    'Conversation, explanation, drafting, and reasoning in the current chat.',
              ),
              const SizedBox(height: 12),
              const _CapabilityCard(
                icon: Icons.compare_arrows_rounded,
                title: 'Gmail message → Salesforce Account description',
                body:
                    'Creates a reviewable enrichment proposal from an exact Gmail message ID and a Salesforce Account ID.',
                badge: 'Approval required',
              ),
              const SizedBox(height: 30),
              const _SectionLabel('BOUNDARIES'),
              const SizedBox(height: 12),
              const _BoundaryCard(),
              if (!connected) ...[
                const SizedBox(height: 20),
                _ConnectionNotice(message: statusMessage!),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

final class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.label,
    required this.value,
    this.accent = BrainPalette.textPrimary,
  });

  final String label;
  final String value;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 190,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: BrainType.meta),
          const SizedBox(height: 9),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: BrainType.metric.copyWith(color: accent),
          ),
        ],
      ),
    );
  }
}

final class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Text(text, style: BrainType.meta);
  }
}

final class _CapabilityCard extends StatelessWidget {
  const _CapabilityCard({
    required this.icon,
    required this.title,
    required this.body,
    this.badge,
  });

  final IconData icon;
  final String title;
  final String body;
  final String? badge;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: BrainPalette.signal.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(11),
            ),
            child: Icon(icon, color: BrainPalette.signal, size: 19),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(title, style: BrainType.cardTitle)),
                    if (badge != null) _Badge(label: badge!),
                  ],
                ),
                const SizedBox(height: 7),
                Text(body, style: BrainType.bodyMuted),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

final class _Badge extends StatelessWidget {
  const _Badge({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: BrainPalette.owner.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: BrainPalette.owner.withValues(alpha: 0.3)),
      ),
      child: Text(
        label,
        style: BrainType.metaStrong.copyWith(color: BrainPalette.owner),
      ),
    );
  }
}

final class _BoundaryCard extends StatelessWidget {
  const _BoundaryCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceSunken,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _BoundaryLine('No Gmail search, listing, or sending'),
          SizedBox(height: 11),
          _BoundaryLine('No direct Salesforce writes'),
          SizedBox(height: 11),
          _BoundaryLine('No account, contact, or lead creation'),
        ],
      ),
    );
  }
}

final class _BoundaryLine extends StatelessWidget {
  const _BoundaryLine(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        const Icon(
          Icons.remove_circle_outline_rounded,
          color: BrainPalette.textMuted,
          size: 16,
        ),
        const SizedBox(width: 10),
        Expanded(child: Text(text, style: BrainType.bodyMuted)),
      ],
    );
  }
}

final class _ConnectionNotice extends StatelessWidget {
  const _ConnectionNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.signal.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.signal.withValues(alpha: 0.25)),
      ),
      child: Text(message, style: BrainType.bodyMuted),
    );
  }
}
