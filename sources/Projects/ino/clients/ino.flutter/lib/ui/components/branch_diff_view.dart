import 'package:flutter/material.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/state/branch_bloc.dart';
import 'package:ino_flutter/ui/components/timeline_event_card.dart';

class BranchDiffView extends StatelessWidget {
  const BranchDiffView({
    super.key,
    required this.diff,
    required this.labelA,
    required this.labelB,
  });

  final BranchDiffResult diff;
  final String labelA;
  final String labelB;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Wrap(
            spacing: 8,
            runSpacing: 4,
            children: [
              Chip(
                avatar: Icon(Icons.link, size: 16, color: colorScheme.primary),
                label: Text('${diff.sharedEvents} shared events'),
                backgroundColor: colorScheme.primary.withAlpha(30),
                side: BorderSide(color: colorScheme.primary.withAlpha(80)),
              ),
              Chip(
                avatar:
                    Icon(Icons.call_split, size: 16, color: Colors.orange),
                label: Text(
                  'Diverged after seq ${diff.divergedAfterSequence}',
                ),
                backgroundColor: Colors.orange.withAlpha(30),
                side: BorderSide(color: Colors.orange.withAlpha(80)),
              ),
            ],
          ),
        ),
        Expanded(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: _DiffColumn(
                  label: labelA,
                  color: Colors.blue,
                  events: diff.onlyInA,
                ),
              ),
              const VerticalDivider(width: 1),
              Expanded(
                child: _DiffColumn(
                  label: labelB,
                  color: Colors.orange,
                  events: diff.onlyInB,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _DiffColumn extends StatelessWidget {
  const _DiffColumn({
    required this.label,
    required this.color,
    required this.events,
  });

  final String label;
  final Color color;
  final List<TimelineEntry> events;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(vertical: 8),
          color: color.withAlpha(40),
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: color,
              fontWeight: FontWeight.w700,
              fontSize: 13,
            ),
          ),
        ),
        if (events.isEmpty)
          Expanded(
            child: Center(
              child: Text(
                'No exclusive events',
                style: TextStyle(
                  color: Colors.white.withAlpha(100),
                  fontSize: 13,
                ),
              ),
            ),
          )
        else
          Expanded(
            child: ListView.builder(
              itemCount: events.length,
              itemBuilder: (context, index) {
                return TimelineEventCard(entry: events[index]);
              },
            ),
          ),
      ],
    );
  }
}
