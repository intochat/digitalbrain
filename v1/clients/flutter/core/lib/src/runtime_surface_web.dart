Uri? sameOriginUiBase() {
  final origin = Uri.base.origin;
  if (origin.isEmpty) {
    return null;
  }
  return Uri.parse('$origin/');
}
