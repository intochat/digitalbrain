import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';

final class MemoryWorkspacePersistence implements WorkspacePersistence {
  MemoryWorkspacePersistence({this.value, this.failWrites = false});

  String? value;
  bool failWrites;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String data) async {
    if (failWrites) throw StateError('Device full');
    // A microtask yield: widget tests must end with no pending timer.
    await null;
    value = data;
  }
}
