import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'image_recipe.dart';
import 'image_scene_painter.dart';
import 'image_exporter.dart';

class ImageCanvasSession {
  final undo = <ImageRecipe>[], redo = <ImageRecipe>[];
  ImageRecipe? pendingEdit;
}

class UiImageCanvas extends StatefulWidget {
  const UiImageCanvas({
    super.key,
    required this.bytes,
    required this.recipe,
    required this.sourceWidth,
    required this.sourceHeight,
    required this.onEdit,
    required this.onSave,
    this.saved = false,
    this.onBusyChanged,
    this.session,
    this.exportImage = ImageExporter.renderPng,
  });
  final Uint8List bytes;
  final int sourceWidth, sourceHeight;
  final ImageRecipe recipe;
  final Future<void> Function(ImageRecipe) onEdit;
  final Future<void> Function(Uint8List) onSave;
  final bool saved;
  final ValueChanged<bool>? onBusyChanged;
  final ImageCanvasSession? session;
  final Future<Uint8List> Function(ui.Image, ImageRecipe) exportImage;
  @override
  State<UiImageCanvas> createState() => _UiImageCanvasState();
}

class _UiImageCanvasState extends State<UiImageCanvas> {
  final transform = TransformationController();
  ui.Image? image;
  String tool = 'pan';
  Rect? cropDraft;
  Offset? cropStart;
  Color color = const Color(0xffff6868);
  double width = 5;
  final points = <Offset>[];
  late final history = widget.session ?? ImageCanvasSession();
  List<ImageRecipe> get undo => history.undo;
  List<ImageRecipe> get redo => history.redo;
  bool busy = false;
  String? error;
  Size viewport = Size.zero;
  int generation = 0, viewEpoch = 0;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void didUpdateWidget(UiImageCanvas old) {
    super.didUpdateWidget(old);
    if (!identical(old.bytes, widget.bytes)) {
      load();
    }
  }

  Future<void> load() async {
    final request = ++generation;
    try {
      if (widget.bytes.length > 32 * 1024 * 1024 ||
          widget.sourceWidth <= 0 ||
          widget.sourceHeight <= 0 ||
          widget.sourceWidth * widget.sourceHeight > 40000000) {
        throw StateError('Image exceeds the supported size.');
      }
      final codec = await ui.instantiateImageCodec(widget.bytes);
      try {
        final decoded = (await codec.getNextFrame()).image;
        if (!mounted || request != generation) {
          decoded.dispose();
          return;
        }
        if (decoded.width != widget.sourceWidth ||
            decoded.height != widget.sourceHeight) {
          decoded.dispose();
          throw StateError(
            'Decoded image dimensions do not match its document.',
          );
        }
        final previous = image;
        setState(() {
          image = decoded;
          error = null;
        });
        previous?.dispose();
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted) fit();
        });
      } finally {
        codec.dispose();
      }
    } catch (e) {
      if (mounted && request == generation) setState(() => error = '$e');
    }
  }

  void fit() {
    final img = image;
    if (img == null || viewport.isEmpty) return;
    final scale = ((viewport.width - 48) / img.width)
        .clamp(.01, 8.0)
        .clamp(.01, ((viewport.height - 48) / img.height).clamp(.01, 8.0));
    resetView(
      Matrix4.identity()
        ..translateByDouble(
          (viewport.width - img.width * scale) / 2,
          (viewport.height - img.height * scale) / 2,
          0,
          1,
        )
        ..scaleByDouble(scale, scale, 1, 1),
    );
  }

  void resetView(Matrix4 value) {
    // Replacing the viewer disposes its fling animation before applying an explicit view.
    setState(() => viewEpoch++);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) transform.value = value;
    });
  }

  Future<void> commit(ImageRecipe recipe, {bool history = true}) async {
    if (busy) return;
    final previous = widget.recipe;
    final onEdit = widget.onEdit;
    final onBusyChanged = widget.onBusyChanged;
    onBusyChanged?.call(true);
    this.history.pendingEdit = recipe;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await onEdit(recipe);
      this.history.pendingEdit = null;
      if (history) {
        undo.add(previous);
        if (undo.length > 100) undo.removeAt(0);
        redo.clear();
      }
    } catch (e) {
      if (mounted) setState(() => error = 'Could not save edits: $e');
    } finally {
      onBusyChanged?.call(false);
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> crop() async {
    final img = image;
    if (img == null) return;
    final rect =
        widget.recipe.crop ??
        Rect.fromLTWH(0, 0, img.width.toDouble(), img.height.toDouble());
    final values = [
      rect.left,
      rect.top,
      rect.width,
      rect.height,
    ].map((v) => TextEditingController(text: '${v.round()}')).toList();
    String? invalid;
    final result = await showDialog<Rect>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: const Text('Crop image'),
          content: SizedBox(
            width: 320,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                for (var i = 0; i < 4; i++)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: TextField(
                      controller: values[i],
                      keyboardType: TextInputType.number,
                      decoration: InputDecoration(
                        labelText: ['X', 'Y', 'Width', 'Height'][i],
                      ),
                    ),
                  ),
                if (invalid != null)
                  Text(
                    invalid!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                final n = values.map((c) => int.tryParse(c.text)).toList();
                if (n.any((v) => v == null) ||
                    n[0]! < 0 ||
                    n[1]! < 0 ||
                    n[2]! <= 0 ||
                    n[3]! <= 0 ||
                    n[0]! + n[2]! > img.width ||
                    n[1]! + n[3]! > img.height) {
                  update(() => invalid = 'Enter a crop inside the image.');
                  return;
                }
                Navigator.pop(
                  context,
                  Rect.fromLTWH(
                    n[0]!.toDouble(),
                    n[1]!.toDouble(),
                    n[2]!.toDouble(),
                    n[3]!.toDouble(),
                  ),
                );
              },
              child: const Text('Apply crop'),
            ),
          ],
        ),
      ),
    );
    // Dialog controllers remain alive until its closing animation has finished.
    if (result != null && mounted) {
      await commit(ImageRecipe(crop: result, strokes: widget.recipe.strokes));
    }
    await Future<void>.delayed(const Duration(milliseconds: 250));
    for (final c in values) {
      c.dispose();
    }
  }

  Offset point(Offset local) {
    final p = transform.toScene(local);
    return Offset(
      p.dx.clamp(0, image!.width.toDouble()),
      p.dy.clamp(0, image!.height.toDouble()),
    );
  }

  Widget action(
    String label,
    IconData icon,
    VoidCallback? callback, {
    bool selected = false,
  }) => IconButton(
    tooltip: label,
    isSelected: selected,
    onPressed: busy ? null : callback,
    icon: Icon(icon, size: 19),
  );
  @override
  Widget build(BuildContext context) => Column(
    children: [
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        child: Wrap(
          spacing: 3,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            action(
              'Pan',
              Icons.pan_tool_outlined,
              () => setState(() => tool = 'pan'),
              selected: tool == 'pan',
            ),
            action(
              'Draw',
              Icons.edit_outlined,
              () => setState(() => tool = 'pen'),
              selected: tool == 'pen',
            ),
            action('Crop', Icons.crop, image == null ? null : crop),
            action(
              'Select crop',
              Icons.crop_free,
              image == null
                  ? null
                  : () => setState(() {
                      tool = 'crop';
                      cropDraft = widget.recipe.crop;
                    }),
              selected: tool == 'crop',
            ),
            action(
              'Undo',
              Icons.undo,
              undo.isEmpty
                  ? null
                  : () async {
                      final previous = undo.last;
                      final current = widget.recipe;
                      await commit(previous, history: false);
                      if (error == null) {
                        undo.removeLast();
                        redo.add(current);
                        setState(() {});
                      }
                    },
            ),
            action(
              'Redo',
              Icons.redo,
              redo.isEmpty
                  ? null
                  : () async {
                      final next = redo.last;
                      final current = widget.recipe;
                      await commit(next, history: false);
                      if (error == null) {
                        redo.removeLast();
                        undo.add(current);
                        setState(() {});
                      }
                    },
            ),
            action(
              'Reset edits',
              Icons.restart_alt,
              () => commit(const ImageRecipe()),
            ),
            TextButton(onPressed: fit, child: const Text('Fit')),
            TextButton(
              onPressed: () => resetView(Matrix4.identity()),
              child: const Text('100%'),
            ),
            if (tool == 'pen') ...[
              for (final c in [
                const Color(0xffff6868),
                const Color(0xffa4e1ca),
                Colors.white,
                Colors.black,
              ])
                IconButton(
                  tooltip: 'Pen color ${c.toARGB32().toRadixString(16)}',
                  onPressed: () => setState(() => color = c),
                  icon: Icon(
                    color == c ? Icons.check_circle : Icons.circle,
                    color: c,
                    size: 20,
                  ),
                ),
              PopupMenuButton<double>(
                tooltip: 'Pen width',
                initialValue: width,
                onSelected: (value) => setState(() => width = value),
                itemBuilder: (_) => [
                  for (final value in [1.0, 3.0, 5.0, 10.0, 20.0, 30.0])
                    PopupMenuItem(
                      value: value,
                      child: Text('${value.round()} px'),
                    ),
                ],
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 14,
                    vertical: 12,
                  ),
                  child: Text('${width.round()} px'),
                ),
              ),
            ],
            FilledButton.icon(
              onPressed: busy || image == null
                  ? null
                  : () async {
                      final onSave = widget.onSave;
                      final exportImage = widget.exportImage;
                      final onBusyChanged = widget.onBusyChanged;
                      final recipe = widget.recipe;
                      onBusyChanged?.call(true);
                      setState(() {
                        busy = true;
                        error = null;
                      });
                      try {
                        await onSave(await exportImage(image!, recipe));
                      } catch (e) {
                        if (mounted) {
                          setState(() => error = 'Could not save copy: $e');
                        }
                      } finally {
                        onBusyChanged?.call(false);
                        if (mounted) setState(() => busy = false);
                      }
                    },
              icon: const Icon(Icons.save_outlined, size: 18),
              label: Text(busy ? 'Saving…' : 'Save copy'),
            ),
          ],
        ),
      ),
      if (tool == 'crop')
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Row(
            children: [
              const Expanded(child: Text('Drag a rectangle on the image')),
              TextButton(
                onPressed: busy
                    ? null
                    : () => setState(() {
                        tool = 'pan';
                        cropDraft = null;
                      }),
                child: const Text('Cancel crop'),
              ),
              FilledButton(
                onPressed: busy || cropDraft == null
                    ? null
                    : () async {
                        await commit(
                          ImageRecipe(
                            crop: cropDraft,
                            strokes: widget.recipe.strokes,
                          ),
                        );
                        if (mounted && error == null) {
                          setState(() {
                            tool = 'pan';
                            cropDraft = null;
                          });
                        }
                      },
                child: const Text('Apply selection'),
              ),
            ],
          ),
        ),
      if (error != null)
        Padding(
          padding: const EdgeInsets.all(8),
          child: Text(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
      if (history.pendingEdit != null)
        TextButton(
          onPressed: busy ? null : () => commit(history.pendingEdit!),
          child: const Text('Retry pending edit'),
        ),
      Expanded(
        child: LayoutBuilder(
          builder: (context, constraints) {
            viewport = constraints.biggest;
            if (image == null) {
              return error == null
                  ? const Center(child: CircularProgressIndicator())
                  : const Center(child: Text('Image unavailable'));
            }
            return Semantics(
              label: 'Image canvas',
              role: ui.SemanticsRole.region,
              container: true,
              child: ClipRect(
                child: ColoredBox(
                  color: const Color(0xff101516),
                  child: Listener(
                    onPointerDown: (e) {
                      if (tool == 'crop' && !busy) {
                        setState(() {
                          cropStart = point(e.localPosition);
                          cropDraft = null;
                        });
                      }
                      if (tool == 'pen' && !busy) {
                        setState(() {
                          points.clear();
                          points.add(point(e.localPosition));
                        });
                      }
                    },
                    onPointerMove: (e) {
                      if (tool == 'crop' && !busy && cropStart != null) {
                        setState(() {
                          final raw = Rect.fromPoints(
                            cropStart!,
                            point(e.localPosition),
                          );
                          final rect = Rect.fromLTRB(
                            raw.left.roundToDouble(),
                            raw.top.roundToDouble(),
                            raw.right.roundToDouble(),
                            raw.bottom.roundToDouble(),
                          );
                          cropDraft = rect.width >= 1 && rect.height >= 1
                              ? rect
                              : null;
                        });
                      }
                      if (tool == 'pen' && !busy && points.isNotEmpty) {
                        setState(() => points.add(point(e.localPosition)));
                      }
                    },
                    onPointerCancel: (_) => setState(points.clear),
                    onPointerUp: (_) {
                      cropStart = null;
                      if (tool == 'pen' && !busy && points.isNotEmpty) {
                        final stroke = PenStroke(
                          color: color,
                          width: width,
                          points: List.of(points),
                        );
                        setState(points.clear);
                        commit(
                          ImageRecipe(
                            crop: widget.recipe.crop,
                            strokes: [...widget.recipe.strokes, stroke],
                          ),
                        );
                      }
                    },
                    child: InteractiveViewer(
                      key: ValueKey(viewEpoch),
                      transformationController: transform,
                      constrained: false,
                      alignment: Alignment.topLeft,
                      boundaryMargin: const EdgeInsets.all(10000),
                      minScale: .01,
                      maxScale: 16,
                      panEnabled: tool == 'pan',
                      scaleEnabled: tool == 'pan',
                      child: CustomPaint(
                        size: Size(
                          image!.width.toDouble(),
                          image!.height.toDouble(),
                        ),
                        painter: ImageScenePainter(
                          image!,
                          cropDraft == null
                              ? widget.recipe
                              : ImageRecipe(
                                  crop: cropDraft,
                                  strokes: widget.recipe.strokes,
                                ),
                          draft: points.isEmpty
                              ? null
                              : PenStroke(
                                  color: color,
                                  width: width,
                                  points: List.of(points),
                                ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            );
          },
        ),
      ),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: Row(
          children: [
            Text(
              image == null
                  ? 'Loading image'
                  : '${image!.width} × ${image!.height}',
              style: Theme.of(context).textTheme.labelSmall,
            ),
            const Spacer(),
            Text(
              widget.saved ? 'Copy saved' : 'Original preserved',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ],
        ),
      ),
    ],
  );
  @override
  void dispose() {
    generation++;
    transform.dispose();
    image?.dispose();
    super.dispose();
  }
}
