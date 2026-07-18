import 'package:flutter_bloc/flutter_bloc.dart';
import 'telemetry.dart';

/// Logs every BLoC event, transition, and error via OpenTelemetry.
/// Register once in main() via Bloc.observer = TelemetryBlocObserver().
class TelemetryBlocObserver extends BlocObserver {
  @override
  void onEvent(Bloc<dynamic, dynamic> bloc, Object? event) {
    super.onEvent(bloc, event);
    if (!InoTelemetry.isInitialized) return;

    InoTelemetry.instance.logger.debug(
      '${bloc.runtimeType} event: ${event.runtimeType}',
      attrs: {
        'bloc': bloc.runtimeType.toString(),
        'event': event.runtimeType.toString(),
      },
    );
  }

  @override
  void onTransition(
    Bloc<dynamic, dynamic> bloc,
    Transition<dynamic, dynamic> transition,
  ) {
    super.onTransition(bloc, transition);
    if (!InoTelemetry.isInitialized) return;

    InoTelemetry.instance.logger.info(
      '${bloc.runtimeType} transition',
      attrs: {
        'bloc': bloc.runtimeType.toString(),
        'event': transition.event.runtimeType.toString(),
      },
    );
  }

  @override
  void onError(BlocBase<dynamic> bloc, Object error, StackTrace stackTrace) {
    super.onError(bloc, error, stackTrace);
    if (!InoTelemetry.isInitialized) return;

    final tel = InoTelemetry.instance;
    tel.logger.error(
      '${bloc.runtimeType} error: $error',
      attrs: {
        'bloc': bloc.runtimeType.toString(),
        'error.message': error.toString(),
      },
    );
    tel.errors.add(1, attributes: {
      'error.type': 'bloc',
      'bloc': bloc.runtimeType.toString(),
    });
  }
}
