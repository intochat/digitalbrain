import 'dart:js_interop';

@JS('KERNEL_PORT')
external JSAny? get _kernelPort;

String? getEnv(String key) {
  if (key == 'KERNEL_PORT') {
    final value = _kernelPort?.dartify();
    if (value != null) {
      return value.toString();
    }
  }
  return null;
}
