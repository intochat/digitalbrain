import 'dart:js' as js;

String? getEnv(String key) {
  if (key == 'KERNEL_PORT') {
    final val = js.context['KERNEL_PORT'];
    if (val != null) {
      return val.toString();
    }
  }
  return null;
}
