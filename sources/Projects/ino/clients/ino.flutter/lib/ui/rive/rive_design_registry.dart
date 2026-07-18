import 'package:flutter/material.dart';
import 'package:rive/rive.dart' as rive;

import 'rive_handles.dart';

abstract interface class RiveDesignRegistry {
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  });
}

abstract class RiveFileLoader {
  Future<rive.File?> load(String assetPath);
}

class AssetRiveFileLoader implements RiveFileLoader {
  @override
  Future<rive.File?> load(String assetPath) async {
    try {
      return await rive.File.asset(assetPath, riveFactory: rive.Factory.rive);
    } on rive.RiveException {
      return null;
    }
  }
}

class AssetRiveDesignRegistry implements RiveDesignRegistry {
  AssetRiveDesignRegistry({RiveFileLoader? loader})
      : _loader = loader ?? AssetRiveFileLoader() {
    _ready = _preloadKernel();
  }

  static const String _kernel = 'kernel';
  static const String _kernelAsset = 'assets/rive/ino-design.riv';

  final RiveFileLoader _loader;
  final Map<String, rive.File> _filesByDomain = {};
  late final Future<void> _ready;

  Future<void> get ready => _ready;

  Future<void> _preloadKernel() async {
    final file = await _loader.load(_kernelAsset);
    if (file != null) _filesByDomain[_kernel] = file;
  }

  String _assetPath(String domain) =>
      domain == _kernel ? _kernelAsset : 'assets/rive/$domain-design.riv';

  Future<rive.File?> _ensure(String domain) async {
    if (_filesByDomain.containsKey(domain)) return _filesByDomain[domain];
    final file = await _loader.load(_assetPath(domain));
    if (file != null) _filesByDomain[domain] = file;
    return file;
  }

  // Test seam — returns the file that would be used for resolution,
  // applying the kernel-fallback rule.
  rive.File? resolvedFileFor({
    required String domain,
    required String artboard,
  }) {
    return _filesByDomain[domain] ?? _filesByDomain[_kernel];
  }

  @override
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  }) async {
    await _ready;
    final file = await _ensure(domain) ?? _filesByDomain[_kernel];
    if (file == null) {
      throw StateError(
        'No Rive design file available for domain="$domain" '
        '(kernel baseline missing too).',
      );
    }
    final controller = rive.RiveWidgetController(
      file,
      artboardSelector: rive.ArtboardSelector.byName(artboard),
    );
    final vmi = controller.dataBind(rive.DataBind.byName(artboard));
    return _LiveResolution(controller, vmi);
  }
}

class _LiveResolution implements RiveResolution {
  _LiveResolution(this._controller, this._vmi)
      : viewModel = _LiveViewModelHandle(_vmi);

  final rive.RiveWidgetController _controller;
  final rive.ViewModelInstance _vmi;

  @override
  final ViewModelHandle viewModel;

  @override
  Widget buildWidget() =>
      rive.RiveWidget(controller: _controller, fit: rive.Fit.layout);

  @override
  void dispose() {
    viewModel.dispose();
    _vmi.dispose();
    _controller.dispose();
  }
}

class _LiveViewModelHandle implements ViewModelHandle {
  _LiveViewModelHandle(this._vmi);

  final rive.ViewModelInstance _vmi;
  final List<rive.ViewModelInstanceTrigger> _triggers = [];
  final Map<String, rive.ViewModelInstanceString> _strings = {};
  final Map<String, rive.ViewModelInstanceNumber> _numbers = {};
  final Map<String, rive.ViewModelInstanceColor> _colors = {};
  final Map<String, rive.ViewModelInstanceEnum> _enums = {};

  @override
  void writeString(String name, String value) {
    final handle = _strings[name] ?? _vmi.string(name);
    if (handle == null) return;
    _strings[name] = handle;
    handle.value = value;
  }

  // anim is advisory only — designer-authored Rive state machines own
  // visual timing. Tween params from the LLM are dropped for live Rive
  // and honored only by DemoRiveDesignRegistry's _TweenedNumber.
  @override
  void writeNumber(String name, double value, {AnimSpec? anim}) {
    final handle = _numbers[name] ?? _vmi.number(name);
    if (handle == null) return;
    _numbers[name] = handle;
    handle.value = value;
  }

  @override
  void writeColor(String name, Color value, {AnimSpec? anim}) {
    final handle = _colors[name] ?? _vmi.color(name);
    if (handle == null) return;
    _colors[name] = handle;
    handle.value = value;
  }

  @override
  void writeEnum(String name, String value) {
    final handle = _enums[name] ?? _vmi.enumerator(name);
    if (handle == null) return;
    _enums[name] = handle;
    handle.value = value;
  }

  @override
  void onTrigger(String name, VoidCallback handler) {
    final t = _vmi.trigger(name);
    if (t == null) return;
    t.addListener((_) => handler());
    _triggers.add(t);
  }

  @override
  void dispose() {
    for (final h in _strings.values) {
      h.dispose();
    }
    for (final h in _numbers.values) {
      h.dispose();
    }
    for (final h in _colors.values) {
      h.dispose();
    }
    for (final h in _enums.values) {
      h.dispose();
    }
    for (final t in _triggers) {
      t.dispose();
    }
    _strings.clear();
    _numbers.clear();
    _colors.clear();
    _enums.clear();
    _triggers.clear();
  }
}
