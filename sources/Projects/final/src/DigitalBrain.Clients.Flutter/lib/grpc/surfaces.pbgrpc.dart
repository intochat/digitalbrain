// Manual gRPC client stubs for SurfaceStream (real gRPC over the proto defined in Kernel/Protos/Surfaces.proto).
// Matches the C# generated service so that SubscribeSurfaces and SendClientEvent are real protobuf calls.

import 'package:grpc/grpc.dart';
import 'surfaces.pb.dart';

class SurfaceStreamClient {
  final ClientChannel _channel;

  SurfaceStreamClient(this._channel);

  ResponseStream<UiSurfaceMessage> subscribeSurfaces(SurfaceSubscription request, {CallOptions? options}) {
    final call = _channel.createCall(
      ClientMethod<SurfaceSubscription, UiSurfaceMessage>(
        '/digitalbrain.surfaces.SurfaceStream/SubscribeSurfaces',
        (value) => value.writeToBuffer(),
        (value) => UiSurfaceMessage.fromBuffer(value),
      ),
      Stream.fromIterable([request]),
      options ?? CallOptions(timeout: Duration(minutes: 60)),
    );
    return ResponseStream<UiSurfaceMessage>(call);
  }

  Future<ClientEventResponse> sendClientEvent(ClientEvent request, {CallOptions? options}) async {
    final call = _channel.createCall(
      ClientMethod<ClientEvent, ClientEventResponse>(
        '/digitalbrain.surfaces.SurfaceStream/SendClientEvent',
        (value) => value.writeToBuffer(),
        (value) => ClientEventResponse.fromBuffer(value),
      ),
      Stream.fromIterable([request]),
      options ?? CallOptions(),
    );

    final response = await call.response.first;
    return response;
  }

  Future<LoginResponse> login(LoginRequest request, {CallOptions? options}) async {
    final call = _channel.createCall(
      ClientMethod<LoginRequest, LoginResponse>(
        '/digitalbrain.surfaces.SurfaceStream/Login',
        (value) => value.writeToBuffer(),
        (value) => LoginResponse.fromBuffer(value),
      ),
      Stream.fromIterable([request]),
      options ?? CallOptions(),
    );
    final response = await call.response.first;
    return response;
  }

  Future<BrainDescriptor> addBrain(AddBrainRequest request, {CallOptions? options}) async {
    final call = _channel.createCall(
      ClientMethod<AddBrainRequest, BrainDescriptor>(
        '/digitalbrain.surfaces.SurfaceStream/AddBrain',
        (value) => value.writeToBuffer(),
        (value) => BrainDescriptor.fromBuffer(value),
      ),
      Stream.fromIterable([request]),
      options ?? CallOptions(),
    );
    final response = await call.response.first;
    return response;
  }

  Future<ClientEventResponse> archiveBrain(ArchiveBrainRequest request, {CallOptions? options}) async {
    final call = _channel.createCall(
      ClientMethod<ArchiveBrainRequest, ClientEventResponse>(
        '/digitalbrain.surfaces.SurfaceStream/ArchiveBrain',
        (value) => value.writeToBuffer(),
        (value) => ClientEventResponse.fromBuffer(value),
      ),
      Stream.fromIterable([request]),
      options ?? CallOptions(),
    );
    final response = await call.response.first;
    return response;
  }
}