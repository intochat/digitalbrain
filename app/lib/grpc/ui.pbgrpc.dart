import 'dart:async' as $async;
import 'dart:core' as $core;

import 'package:grpc/service_api.dart' as $grpc;
import 'package:protobuf/protobuf.dart' as $pb;

import 'ui.pb.dart' as $0;

export 'ui.pb.dart';

@$pb.GrpcServiceName('digitalbrain.v2.ui.DigitalBrainV2Ui')
class DigitalBrainV2UiClient extends $grpc.Client {
  static const $core.String defaultHost = '';

  static const $core.List<$core.String> oauthScopes = [''];

  DigitalBrainV2UiClient(super.channel, {super.options, super.interceptors});

  $grpc.ResponseFuture<$0.SessionReply> bootstrapSession(
    $0.BootstrapSessionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$bootstrapSession, request, options: options);
  }

  $grpc.ResponseFuture<$0.SessionReply> refreshSession(
    $0.RefreshSessionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$refreshSession, request, options: options);
  }

  $grpc.ResponseStream<$0.SurfaceFeedEvent> watchSurfaceFeed(
    $0.WatchSurfaceFeedRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(
      _$watchSurfaceFeed,
      $async.Stream.fromIterable([request]),
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    $0.AcknowledgeSurfaceFeedRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(
      _$acknowledgeSurfaceFeed,
      request,
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.SubmitActionReply> submitAction(
    $0.SubmitActionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$submitAction, request, options: options);
  }

  $grpc.ResponseFuture<$0.LogoutSessionReply> logoutSession(
    $0.LogoutSessionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$logoutSession, request, options: options);
  }

  static final _$bootstrapSession =
      $grpc.ClientMethod<$0.BootstrapSessionRequest, $0.SessionReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/BootstrapSession',
        ($0.BootstrapSessionRequest value) => value.writeToBuffer(),
        $0.SessionReply.fromBuffer,
      );
  static final _$refreshSession =
      $grpc.ClientMethod<$0.RefreshSessionRequest, $0.SessionReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/RefreshSession',
        ($0.RefreshSessionRequest value) => value.writeToBuffer(),
        $0.SessionReply.fromBuffer,
      );
  static final _$watchSurfaceFeed =
      $grpc.ClientMethod<$0.WatchSurfaceFeedRequest, $0.SurfaceFeedEvent>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/WatchSurfaceFeed',
        ($0.WatchSurfaceFeedRequest value) => value.writeToBuffer(),
        $0.SurfaceFeedEvent.fromBuffer,
      );
  static final _$acknowledgeSurfaceFeed =
      $grpc.ClientMethod<
        $0.AcknowledgeSurfaceFeedRequest,
        $0.AcknowledgeSurfaceFeedReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/AcknowledgeSurfaceFeed',
        ($0.AcknowledgeSurfaceFeedRequest value) => value.writeToBuffer(),
        $0.AcknowledgeSurfaceFeedReply.fromBuffer,
      );
  static final _$submitAction =
      $grpc.ClientMethod<$0.SubmitActionRequest, $0.SubmitActionReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/SubmitAction',
        ($0.SubmitActionRequest value) => value.writeToBuffer(),
        $0.SubmitActionReply.fromBuffer,
      );
  static final _$logoutSession =
      $grpc.ClientMethod<$0.LogoutSessionRequest, $0.LogoutSessionReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/LogoutSession',
        ($0.LogoutSessionRequest value) => value.writeToBuffer(),
        $0.LogoutSessionReply.fromBuffer,
      );
}

@$pb.GrpcServiceName('digitalbrain.v2.ui.DigitalBrainV2Ui')
abstract class DigitalBrainV2UiServiceBase extends $grpc.Service {
  $core.String get $name => 'digitalbrain.v2.ui.DigitalBrainV2Ui';

  DigitalBrainV2UiServiceBase() {
    $addMethod(
      $grpc.ServiceMethod<$0.BootstrapSessionRequest, $0.SessionReply>(
        'BootstrapSession',
        bootstrapSession_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.BootstrapSessionRequest.fromBuffer(value),
        ($0.SessionReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.RefreshSessionRequest, $0.SessionReply>(
        'RefreshSession',
        refreshSession_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.RefreshSessionRequest.fromBuffer(value),
        ($0.SessionReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.WatchSurfaceFeedRequest, $0.SurfaceFeedEvent>(
        'WatchSurfaceFeed',
        watchSurfaceFeed_Pre,
        false,
        true,
        ($core.List<$core.int> value) =>
            $0.WatchSurfaceFeedRequest.fromBuffer(value),
        ($0.SurfaceFeedEvent value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.AcknowledgeSurfaceFeedRequest,
        $0.AcknowledgeSurfaceFeedReply
      >(
        'AcknowledgeSurfaceFeed',
        acknowledgeSurfaceFeed_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.AcknowledgeSurfaceFeedRequest.fromBuffer(value),
        ($0.AcknowledgeSurfaceFeedReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.SubmitActionRequest, $0.SubmitActionReply>(
        'SubmitAction',
        submitAction_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.SubmitActionRequest.fromBuffer(value),
        ($0.SubmitActionReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.LogoutSessionRequest, $0.LogoutSessionReply>(
        'LogoutSession',
        logoutSession_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.LogoutSessionRequest.fromBuffer(value),
        ($0.LogoutSessionReply value) => value.writeToBuffer(),
      ),
    );
  }

  $async.Future<$0.SessionReply> bootstrapSession_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.BootstrapSessionRequest> $request,
  ) async {
    return bootstrapSession($call, await $request);
  }

  $async.Future<$0.SessionReply> bootstrapSession(
    $grpc.ServiceCall call,
    $0.BootstrapSessionRequest request,
  );

  $async.Future<$0.SessionReply> refreshSession_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.RefreshSessionRequest> $request,
  ) async {
    return refreshSession($call, await $request);
  }

  $async.Future<$0.SessionReply> refreshSession(
    $grpc.ServiceCall call,
    $0.RefreshSessionRequest request,
  );

  $async.Stream<$0.SurfaceFeedEvent> watchSurfaceFeed_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.WatchSurfaceFeedRequest> $request,
  ) async* {
    yield* watchSurfaceFeed($call, await $request);
  }

  $async.Stream<$0.SurfaceFeedEvent> watchSurfaceFeed(
    $grpc.ServiceCall call,
    $0.WatchSurfaceFeedRequest request,
  );

  $async.Future<$0.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.AcknowledgeSurfaceFeedRequest> $request,
  ) async {
    return acknowledgeSurfaceFeed($call, await $request);
  }

  $async.Future<$0.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    $grpc.ServiceCall call,
    $0.AcknowledgeSurfaceFeedRequest request,
  );

  $async.Future<$0.SubmitActionReply> submitAction_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.SubmitActionRequest> $request,
  ) async {
    return submitAction($call, await $request);
  }

  $async.Future<$0.SubmitActionReply> submitAction(
    $grpc.ServiceCall call,
    $0.SubmitActionRequest request,
  );

  $async.Future<$0.LogoutSessionReply> logoutSession_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.LogoutSessionRequest> $request,
  ) async {
    return logoutSession($call, await $request);
  }

  $async.Future<$0.LogoutSessionReply> logoutSession(
    $grpc.ServiceCall call,
    $0.LogoutSessionRequest request,
  );
}
