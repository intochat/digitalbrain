import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'ui_alert.dart';
import 'ui_avatar.dart';
import 'ui_badge.dart';
import 'ui_bottom_nav.dart';
import 'ui_breadcrumb.dart';
import 'ui_button.dart';
import 'ui_checkbox.dart';
import 'ui_column.dart';
import 'ui_date_field.dart';
import 'ui_dialog.dart';
import 'ui_divider.dart';
import 'ui_gap.dart';
import 'ui_header.dart';
import 'ui_heading.dart';
import 'ui_icon.dart';
import 'ui_list.dart';
import 'ui_pagination.dart';
import 'ui_panel.dart';
import 'ui_progress.dart';
import 'ui_radio_group.dart';
import 'ui_row.dart';
import 'ui_screen.dart';
import 'ui_select.dart';
import 'ui_sheet.dart';
import 'ui_slider.dart';
import 'ui_sidebar.dart';
import 'ui_spinner.dart';
import 'ui_switch.dart';
import 'ui_tabs.dart';
import 'ui_text.dart';
import 'ui_text_area.dart';
import 'ui_text_field.dart';
import 'ui_tile.dart';
import 'ui_toast.dart';
import 'ui_tooltip.dart';

// Maps a ui:* node (type already lower-cased by the tree renderer) to its ForUI cover widget.
// [buildChild] recurses back into the tree renderer for container children (ui:Screen, ui:Panel).
// Returns SizedBox.shrink() for unknown types — Task 6 expects a non-null Widget.
Widget buildUiNode(
  String type,
  Map<String, Object?> props,
  List childrenList,
  RemoteEventHandler onEvent, {
  required Widget Function(Map<String, Object?>) buildChild,
}) {
  List<Widget> kids() =>
      childrenList.cast<Map<String, Object?>>().map(buildChild).toList();
  String s(String key) => (props[key] ?? '').toString();
  List<String> optList(String key) =>
      (props[key] as List?)?.map((e) => e.toString()).toList() ?? const [];
  double d(String key) => (props[key] as num?)?.toDouble() ?? 0;
  List itemList() => (props['items'] as List?) ?? const [];

  switch (type.toLowerCase()) {
    case 'ui:screen':
      return UiKitScreen(children: kids());
    case 'ui:panel':
      return UiKitPanel(children: kids());
    case 'ui:text':
      return UiKitText(text: s('text'));
    case 'ui:textfield':
      return UiKitTextField(
        name: s('name'),
        placeholder: s('placeholder'),
        secret: props['secret'] == true,
      );
    case 'ui:button':
      return UiKitButton(
        label: s('label'),
        pack: s('pack'),
        experienceId: s('experienceId'),
        eventName: s('eventName'),
        // synapseType lets config-form buttons emit ConfigurationProvided instead of the default ExperienceStep.
        synapseType: props['synapseType']?.toString().isNotEmpty == true
            ? props['synapseType']!.toString()
            : 'ExperienceStep',
        onEvent: onEvent,
      );
    case 'ui:checkbox':
      return UiKitCheckbox(name: s('name'), label: s('label'));
    case 'ui:switch':
      return UiKitSwitch(name: s('name'), label: s('label'));
    case 'ui:textarea':
      return UiKitTextArea(name: s('name'), placeholder: s('placeholder'));
    case 'ui:select':
      // Backend emits 'items'; older surfaces may use 'options'. Prefer 'items'.
      final selectOptions = optList('items').isNotEmpty ? optList('items') : optList('options');
      return UiKitSelect(name: s('name'), options: selectOptions, label: s('label'));
    case 'ui:radiogroup':
      return UiKitRadioGroup(name: s('name'), options: optList('options'), label: s('label'));
    case 'ui:slider':
      return UiKitSlider(name: s('name'), min: d('min'), max: d('max'), label: s('label'));
    case 'ui:datefield':
      return UiKitDateField(name: s('name'), label: s('label'));
    case 'ui:row':
      return UiKitRow(children: kids());
    case 'ui:column':
      return UiKitColumn(children: kids());
    case 'ui:divider':
      return const UiKitDivider();
    case 'ui:header':
      return UiKitHeader(title: s('title'));
    case 'ui:gap':
      return UiKitGap(size: d('size') == 0 ? 16 : d('size'));
    case 'ui:heading':
      return UiKitHeading(text: s('text'));
    case 'ui:icon':
      return UiKitIcon(name: s('name'));
    case 'ui:avatar':
      return UiKitAvatar(imageUrl: s('imageUrl'), fallback: s('fallback'));
    case 'ui:badge':
      return UiKitBadge(text: s('text'));
    case 'ui:tile':
      return UiKitTile(
        title: s('title'), subtitle: s('subtitle'),
        pack: s('pack'), experienceId: s('experienceId'), eventName: s('eventName'),
        onEvent: onEvent,
      );
    case 'ui:list':
      return UiKitList(
        tileDescriptors: childrenList.cast<Map<String, Object?>>().toList(),
        onEvent: onEvent,
      );
    case 'ui:tabs':
      return UiKitTabs(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:breadcrumb':
      return UiKitBreadcrumb(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:sidebar':
      return UiKitSidebar(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:bottomnav':
      return UiKitBottomNav(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:pagination':
      return UiKitPagination(items: itemList(), pack: s('pack'), experienceId: s('experienceId'), onEvent: onEvent);
    case 'ui:alert':
      return UiKitAlert(title: s('title'), subtitle: s('subtitle'));
    case 'ui:progress':
      return UiKitProgress(value: d('value'));
    case 'ui:spinner':
      return const UiKitSpinner();
    case 'ui:tooltip':
      final tipKids = kids();
      return UiKitTooltip(tip: s('tip'), child: tipKids.isEmpty ? const SizedBox.shrink() : tipKids.first);
    case 'ui:dialog':
      return UiKitDialog(open: props['open'] == true, title: s('title'), children: kids());
    case 'ui:sheet':
      return UiKitSheet(open: props['open'] == true, title: s('title'), children: kids());
    case 'ui:toast':
      return UiKitToast(message: s('message'));
    default:
      return const SizedBox.shrink();
  }
}
