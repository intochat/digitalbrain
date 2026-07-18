import 'package:rfw/rfw.dart';
import 'package:ino_flutter/ui/components/activity_card.dart';
import 'package:ino_flutter/ui/components/chat_bubble.dart';
import 'package:ino_flutter/ui/components/event_card.dart';
import 'package:ino_flutter/ui/components/flight_card.dart';
import 'package:ino_flutter/ui/components/hotel_card.dart';
import 'package:ino_flutter/ui/components/place_card.dart';
import 'package:ino_flutter/ui/components/trip_summary_card.dart';
import 'package:ino_flutter/ui/components/weather_summary_card.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:ino_flutter/ui/rive/rive_widgets.dart';

class InoRuntime {
  InoRuntime(this.runtime, this._libraries);

  final Runtime runtime;
  final Map<LibraryName, WidgetLibrary> _libraries;

  WidgetLibrary? libraryNamed(LibraryName name) => _libraries[name];
}

InoRuntime createInoRuntime({RiveDesignRegistry? riveRegistry}) {
  final runtime = Runtime();
  final libraries = <LibraryName, WidgetLibrary>{};

  void register(List<String> name, WidgetLibrary lib) {
    final n = LibraryName(name);
    runtime.update(n, lib);
    libraries[n] = lib;
  }

  register(<String>['core', 'widgets'], createCoreWidgets());
  register(<String>['material', 'widgets'], createMaterialWidgets());
  register(<String>['ino', 'chat'], createChatWidgets());
  register(<String>['ino', 'flights'], createFlightWidgets());
  register(<String>['ino', 'hotels'], createHotelWidgets());
  register(<String>['ino', 'places'], createPlaceWidgets());
  register(<String>['ino', 'weather'], createWeatherWidgets());
  register(<String>['ino', 'events'], createEventWidgets());
  register(<String>['ino', 'activities'], createActivityWidgets());
  register(<String>['ino', 'summary'], createSummaryWidgets());
  if (riveRegistry != null) {
    register(<String>['ino', 'rive'], createRiveWidgets(riveRegistry));
  }
  return InoRuntime(runtime, libraries);
}
