import 'activity_models.dart';

String formatActivityTimestamp(DateTime value) {
  final utc = value.toUtc();
  return '${utc.year.toString().padLeft(4, '0')}-'
      '${utc.month.toString().padLeft(2, '0')}-'
      '${utc.day.toString().padLeft(2, '0')} '
      '${utc.hour.toString().padLeft(2, '0')}:'
      '${utc.minute.toString().padLeft(2, '0')}:'
      '${utc.second.toString().padLeft(2, '0')} UTC';
}

String formatActivityDuration(ActivityRun run) {
  final elapsed = run.elapsed;
  if (elapsed == null) {
    return switch (run.status) {
      ActivityStatus.queued => 'Not started',
      ActivityStatus.running ||
      ActivityStatus.waitingForApproval => 'In progress',
      _ => 'Not available',
    };
  }
  if (elapsed.inMinutes >= 1) {
    final seconds = elapsed.inSeconds.remainder(60);
    return '${elapsed.inMinutes}m ${seconds}s';
  }
  if (elapsed.inSeconds >= 1) return '${elapsed.inSeconds}s';
  return '${elapsed.inMilliseconds}ms';
}
