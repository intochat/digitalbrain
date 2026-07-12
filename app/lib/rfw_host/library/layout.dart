part of 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

Widget _divider() => Container(
  height: 1,
  color: DigitalBrainColors.hairline,
  margin: const EdgeInsets.symmetric(vertical: 4),
);

Widget _stack(BuildContext c, DataSource s, Axis axis) {
  final kids = s.childList(['children']);
  final gap = _d(s, 'gap', 12);
  final between = _bool(s, 'between', false);
  final equal = _bool(s, 'equal', false);
  final cross = _cross(
    _str(s, 'cross', axis == Axis.vertical ? 'stretch' : 'center'),
  );
  final main = between
      ? MainAxisAlignment.spaceBetween
      : MainAxisAlignment.start;

  var items = kids;
  if (equal) {
    items = [for (final w in kids) Expanded(child: w)];
  }
  var children = items;
  if (!between && gap > 0 && items.length > 1) {
    children = <Widget>[];
    for (var i = 0; i < items.length; i++) {
      if (i > 0) {
        children.add(
          SizedBox(
            width: axis == Axis.horizontal ? gap : 0,
            height: axis == Axis.vertical ? gap : 0,
          ),
        );
      }
      children.add(items[i]);
    }
  }

  if (axis == Axis.vertical) {
    return Column(
      crossAxisAlignment: cross,
      mainAxisAlignment: main,
      mainAxisSize: MainAxisSize.min,
      children: children,
    );
  }
  final row = Row(
    crossAxisAlignment: cross,
    mainAxisAlignment: main,
    mainAxisSize: MainAxisSize.min,
    children: children
        .map((c) => Flexible(fit: FlexFit.loose, child: c))
        .toList(),
  );
  // A Row that stretches its children on the cross (vertical) axis needs a
  // bounded height. RFW content renders inside an unbounded scroll viewport,
  // so give the row a definite height from its children's intrinsics.
  return cross == CrossAxisAlignment.stretch
      ? IntrinsicHeight(child: row)
      : row;
}

Widget _pad(BuildContext c, DataSource s) {
  final all = s.v<double>(['all']) ?? s.v<int>(['all'])?.toDouble();
  final h = _d(s, 'h', all ?? 0);
  final v = _d(s, 'v', all ?? 0);
  return Padding(
    padding: EdgeInsets.symmetric(horizontal: h, vertical: v),
    child: s.child(['child']),
  );
}
