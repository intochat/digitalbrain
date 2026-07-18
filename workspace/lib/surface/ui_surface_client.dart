import 'ui_surface_models.dart';

abstract class UiSurfaceClient {
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId);

  Future<void> sendSurfaceAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  });

  Stream<UiFeedMessage> watch({required int cursor});
}
