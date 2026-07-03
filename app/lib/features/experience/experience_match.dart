/// True when a decoded surface belongs to a live experience. When [target] (pack/experienceId) is
/// supplied (the deep-linked route case), only that experience matches; otherwise any hop matches.
bool experienceHopMatches(Map<String, Object?> data, String? target) {
  final ref = data['activeExperience'];
  if (ref is! String || ref.isEmpty) return false;
  if (target == null || target.isEmpty) return true;
  return ref == target;
}
