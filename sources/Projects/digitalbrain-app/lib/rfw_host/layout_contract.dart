import 'package:flutter/widgets.dart';

/// Every RFW document is rendered inside this frame. RFW trees freely use
/// Row/Expanded/CustomPaint; embedded in an unbounded scroll/overlay they
/// fail `hasSize`. The contract: give the subtree a *tight* width and an
/// intrinsic height. (Generalized from the rfw-gallery cascade fix.)
Widget rfwBoundedFrame({required Widget child, required double width}) {
  return SizedBox(
    width: width,
    child: IntrinsicHeight(child: child),
  );
}
