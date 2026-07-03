import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

class UiKitAvatar extends StatelessWidget {
  const UiKitAvatar({super.key, this.imageUrl = '', this.fallback = ''});
  final String imageUrl;
  final String fallback;

  @override
  Widget build(BuildContext context) => imageUrl.isEmpty
      ? FAvatar.raw(child: Text(fallback))
      : FAvatar(image: NetworkImage(imageUrl), fallback: Text(fallback));
}
