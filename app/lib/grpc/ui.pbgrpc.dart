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

  $grpc.ResponseFuture<$0.FeatureDraftReply> getFeatureDraft(
    $0.GetFeatureDraftRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getFeatureDraft, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureDraftReply> resetFeatureDraftInstallation(
    $0.ResetFeatureDraftInstallationRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(
      _$resetFeatureDraftInstallation,
      request,
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.FeatureDraftReply> reviseFeatureDraft(
    $0.ReviseFeatureDraftRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$reviseFeatureDraft, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureDraftPatchReply> suggestFeatureChange(
    $0.SuggestFeatureChangeRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$suggestFeatureChange, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureReleaseReviewReply> verifyFeatureDraft(
    $0.VerifyFeatureDraftRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$verifyFeatureDraft, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureAccessReviewReply> reviewFeatureAccess(
    $0.ReviewFeatureAccessRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$reviewFeatureAccess, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureInstallReply> installFeatureVersion(
    $0.InstallFeatureVersionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$installFeatureVersion, request, options: options);
  }

  $grpc.ResponseFuture<$0.ResumeOriginatingRequestReply>
  resumeOriginatingRequest(
    $0.ResumeOriginatingRequestRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(
      _$resumeOriginatingRequest,
      request,
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.ListFeaturesReply> listFeatures(
    $0.ListFeaturesRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$listFeatures, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureReply> getFeature(
    $0.GetFeatureRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getFeature, request, options: options);
  }

  $grpc.ResponseFuture<$0.FeatureReleaseSourceReply> getFeatureReleaseSource(
    $0.GetFeatureReleaseSourceRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(
      _$getFeatureReleaseSource,
      request,
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.FeatureReply> rollbackFeatureVersion(
    $0.RollbackFeatureVersionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(
      _$rollbackFeatureVersion,
      request,
      options: options,
    );
  }

  $grpc.ResponseFuture<$0.ListConnectionsReply> listConnections(
    $0.ListConnectionsRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$listConnections, request, options: options);
  }

  $grpc.ResponseFuture<$0.ConnectionReply> getConnection(
    $0.GetConnectionRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getConnection, request, options: options);
  }

  $grpc.ResponseFuture<$0.ListActivityReply> listActivity(
    $0.ListActivityRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$listActivity, request, options: options);
  }

  $grpc.ResponseFuture<$0.RunReply> getRun(
    $0.GetRunRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getRun, request, options: options);
  }

  $grpc.ResponseFuture<$0.ListMemoryItemsReply> listMemoryItems(
    $0.ListMemoryItemsRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$listMemoryItems, request, options: options);
  }

  $grpc.ResponseFuture<$0.MemoryItemReply> getMemoryItem(
    $0.GetMemoryItemRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getMemoryItem, request, options: options);
  }

  $grpc.ResponseFuture<$0.HomeSummaryReply> getHomeSummary(
    $0.GetHomeSummaryRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getHomeSummary, request, options: options);
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
  static final _$getFeatureDraft =
      $grpc.ClientMethod<$0.GetFeatureDraftRequest, $0.FeatureDraftReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetFeatureDraft',
        ($0.GetFeatureDraftRequest value) => value.writeToBuffer(),
        $0.FeatureDraftReply.fromBuffer,
      );
  static final _$resetFeatureDraftInstallation =
      $grpc.ClientMethod<
        $0.ResetFeatureDraftInstallationRequest,
        $0.FeatureDraftReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ResetFeatureDraftInstallation',
        ($0.ResetFeatureDraftInstallationRequest value) =>
            value.writeToBuffer(),
        $0.FeatureDraftReply.fromBuffer,
      );
  static final _$reviseFeatureDraft =
      $grpc.ClientMethod<$0.ReviseFeatureDraftRequest, $0.FeatureDraftReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ReviseFeatureDraft',
        ($0.ReviseFeatureDraftRequest value) => value.writeToBuffer(),
        $0.FeatureDraftReply.fromBuffer,
      );
  static final _$suggestFeatureChange =
      $grpc.ClientMethod<
        $0.SuggestFeatureChangeRequest,
        $0.FeatureDraftPatchReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/SuggestFeatureChange',
        ($0.SuggestFeatureChangeRequest value) => value.writeToBuffer(),
        $0.FeatureDraftPatchReply.fromBuffer,
      );
  static final _$verifyFeatureDraft =
      $grpc.ClientMethod<
        $0.VerifyFeatureDraftRequest,
        $0.FeatureReleaseReviewReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/VerifyFeatureDraft',
        ($0.VerifyFeatureDraftRequest value) => value.writeToBuffer(),
        $0.FeatureReleaseReviewReply.fromBuffer,
      );
  static final _$reviewFeatureAccess =
      $grpc.ClientMethod<
        $0.ReviewFeatureAccessRequest,
        $0.FeatureAccessReviewReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ReviewFeatureAccess',
        ($0.ReviewFeatureAccessRequest value) => value.writeToBuffer(),
        $0.FeatureAccessReviewReply.fromBuffer,
      );
  static final _$installFeatureVersion =
      $grpc.ClientMethod<
        $0.InstallFeatureVersionRequest,
        $0.FeatureInstallReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/InstallFeatureVersion',
        ($0.InstallFeatureVersionRequest value) => value.writeToBuffer(),
        $0.FeatureInstallReply.fromBuffer,
      );
  static final _$resumeOriginatingRequest =
      $grpc.ClientMethod<
        $0.ResumeOriginatingRequestRequest,
        $0.ResumeOriginatingRequestReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ResumeOriginatingRequest',
        ($0.ResumeOriginatingRequestRequest value) => value.writeToBuffer(),
        $0.ResumeOriginatingRequestReply.fromBuffer,
      );
  static final _$listFeatures =
      $grpc.ClientMethod<$0.ListFeaturesRequest, $0.ListFeaturesReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ListFeatures',
        ($0.ListFeaturesRequest value) => value.writeToBuffer(),
        $0.ListFeaturesReply.fromBuffer,
      );
  static final _$getFeature =
      $grpc.ClientMethod<$0.GetFeatureRequest, $0.FeatureReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetFeature',
        ($0.GetFeatureRequest value) => value.writeToBuffer(),
        $0.FeatureReply.fromBuffer,
      );
  static final _$getFeatureReleaseSource =
      $grpc.ClientMethod<
        $0.GetFeatureReleaseSourceRequest,
        $0.FeatureReleaseSourceReply
      >(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetFeatureReleaseSource',
        ($0.GetFeatureReleaseSourceRequest value) => value.writeToBuffer(),
        $0.FeatureReleaseSourceReply.fromBuffer,
      );
  static final _$rollbackFeatureVersion =
      $grpc.ClientMethod<$0.RollbackFeatureVersionRequest, $0.FeatureReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/RollbackFeatureVersion',
        ($0.RollbackFeatureVersionRequest value) => value.writeToBuffer(),
        $0.FeatureReply.fromBuffer,
      );
  static final _$listConnections =
      $grpc.ClientMethod<$0.ListConnectionsRequest, $0.ListConnectionsReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ListConnections',
        ($0.ListConnectionsRequest value) => value.writeToBuffer(),
        $0.ListConnectionsReply.fromBuffer,
      );
  static final _$getConnection =
      $grpc.ClientMethod<$0.GetConnectionRequest, $0.ConnectionReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetConnection',
        ($0.GetConnectionRequest value) => value.writeToBuffer(),
        $0.ConnectionReply.fromBuffer,
      );
  static final _$listActivity =
      $grpc.ClientMethod<$0.ListActivityRequest, $0.ListActivityReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ListActivity',
        ($0.ListActivityRequest value) => value.writeToBuffer(),
        $0.ListActivityReply.fromBuffer,
      );
  static final _$getRun = $grpc.ClientMethod<$0.GetRunRequest, $0.RunReply>(
    '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetRun',
    ($0.GetRunRequest value) => value.writeToBuffer(),
    $0.RunReply.fromBuffer,
  );
  static final _$listMemoryItems =
      $grpc.ClientMethod<$0.ListMemoryItemsRequest, $0.ListMemoryItemsReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/ListMemoryItems',
        ($0.ListMemoryItemsRequest value) => value.writeToBuffer(),
        $0.ListMemoryItemsReply.fromBuffer,
      );
  static final _$getMemoryItem =
      $grpc.ClientMethod<$0.GetMemoryItemRequest, $0.MemoryItemReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetMemoryItem',
        ($0.GetMemoryItemRequest value) => value.writeToBuffer(),
        $0.MemoryItemReply.fromBuffer,
      );
  static final _$getHomeSummary =
      $grpc.ClientMethod<$0.GetHomeSummaryRequest, $0.HomeSummaryReply>(
        '/digitalbrain.v2.ui.DigitalBrainV2Ui/GetHomeSummary',
        ($0.GetHomeSummaryRequest value) => value.writeToBuffer(),
        $0.HomeSummaryReply.fromBuffer,
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
    $addMethod(
      $grpc.ServiceMethod<$0.GetFeatureDraftRequest, $0.FeatureDraftReply>(
        'GetFeatureDraft',
        getFeatureDraft_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.GetFeatureDraftRequest.fromBuffer(value),
        ($0.FeatureDraftReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.ResetFeatureDraftInstallationRequest,
        $0.FeatureDraftReply
      >(
        'ResetFeatureDraftInstallation',
        resetFeatureDraftInstallation_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ResetFeatureDraftInstallationRequest.fromBuffer(value),
        ($0.FeatureDraftReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.ReviseFeatureDraftRequest, $0.FeatureDraftReply>(
        'ReviseFeatureDraft',
        reviseFeatureDraft_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ReviseFeatureDraftRequest.fromBuffer(value),
        ($0.FeatureDraftReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.SuggestFeatureChangeRequest,
        $0.FeatureDraftPatchReply
      >(
        'SuggestFeatureChange',
        suggestFeatureChange_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.SuggestFeatureChangeRequest.fromBuffer(value),
        ($0.FeatureDraftPatchReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.VerifyFeatureDraftRequest,
        $0.FeatureReleaseReviewReply
      >(
        'VerifyFeatureDraft',
        verifyFeatureDraft_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.VerifyFeatureDraftRequest.fromBuffer(value),
        ($0.FeatureReleaseReviewReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.ReviewFeatureAccessRequest,
        $0.FeatureAccessReviewReply
      >(
        'ReviewFeatureAccess',
        reviewFeatureAccess_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ReviewFeatureAccessRequest.fromBuffer(value),
        ($0.FeatureAccessReviewReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.InstallFeatureVersionRequest,
        $0.FeatureInstallReply
      >(
        'InstallFeatureVersion',
        installFeatureVersion_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.InstallFeatureVersionRequest.fromBuffer(value),
        ($0.FeatureInstallReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.ResumeOriginatingRequestRequest,
        $0.ResumeOriginatingRequestReply
      >(
        'ResumeOriginatingRequest',
        resumeOriginatingRequest_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ResumeOriginatingRequestRequest.fromBuffer(value),
        ($0.ResumeOriginatingRequestReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.ListFeaturesRequest, $0.ListFeaturesReply>(
        'ListFeatures',
        listFeatures_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ListFeaturesRequest.fromBuffer(value),
        ($0.ListFeaturesReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.GetFeatureRequest, $0.FeatureReply>(
        'GetFeature',
        getFeature_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.GetFeatureRequest.fromBuffer(value),
        ($0.FeatureReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<
        $0.GetFeatureReleaseSourceRequest,
        $0.FeatureReleaseSourceReply
      >(
        'GetFeatureReleaseSource',
        getFeatureReleaseSource_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.GetFeatureReleaseSourceRequest.fromBuffer(value),
        ($0.FeatureReleaseSourceReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.RollbackFeatureVersionRequest, $0.FeatureReply>(
        'RollbackFeatureVersion',
        rollbackFeatureVersion_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.RollbackFeatureVersionRequest.fromBuffer(value),
        ($0.FeatureReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.ListConnectionsRequest, $0.ListConnectionsReply>(
        'ListConnections',
        listConnections_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ListConnectionsRequest.fromBuffer(value),
        ($0.ListConnectionsReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.GetConnectionRequest, $0.ConnectionReply>(
        'GetConnection',
        getConnection_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.GetConnectionRequest.fromBuffer(value),
        ($0.ConnectionReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.ListActivityRequest, $0.ListActivityReply>(
        'ListActivity',
        listActivity_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ListActivityRequest.fromBuffer(value),
        ($0.ListActivityReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.GetRunRequest, $0.RunReply>(
        'GetRun',
        getRun_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.GetRunRequest.fromBuffer(value),
        ($0.RunReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.ListMemoryItemsRequest, $0.ListMemoryItemsReply>(
        'ListMemoryItems',
        listMemoryItems_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.ListMemoryItemsRequest.fromBuffer(value),
        ($0.ListMemoryItemsReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.GetMemoryItemRequest, $0.MemoryItemReply>(
        'GetMemoryItem',
        getMemoryItem_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.GetMemoryItemRequest.fromBuffer(value),
        ($0.MemoryItemReply value) => value.writeToBuffer(),
      ),
    );
    $addMethod(
      $grpc.ServiceMethod<$0.GetHomeSummaryRequest, $0.HomeSummaryReply>(
        'GetHomeSummary',
        getHomeSummary_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.GetHomeSummaryRequest.fromBuffer(value),
        ($0.HomeSummaryReply value) => value.writeToBuffer(),
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

  $async.Future<$0.FeatureDraftReply> getFeatureDraft_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetFeatureDraftRequest> $request,
  ) async {
    return getFeatureDraft($call, await $request);
  }

  $async.Future<$0.FeatureDraftReply> getFeatureDraft(
    $grpc.ServiceCall call,
    $0.GetFeatureDraftRequest request,
  );

  $async.Future<$0.FeatureDraftReply> resetFeatureDraftInstallation_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ResetFeatureDraftInstallationRequest> $request,
  ) async {
    return resetFeatureDraftInstallation($call, await $request);
  }

  $async.Future<$0.FeatureDraftReply> resetFeatureDraftInstallation(
    $grpc.ServiceCall call,
    $0.ResetFeatureDraftInstallationRequest request,
  );

  $async.Future<$0.FeatureDraftReply> reviseFeatureDraft_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ReviseFeatureDraftRequest> $request,
  ) async {
    return reviseFeatureDraft($call, await $request);
  }

  $async.Future<$0.FeatureDraftReply> reviseFeatureDraft(
    $grpc.ServiceCall call,
    $0.ReviseFeatureDraftRequest request,
  );

  $async.Future<$0.FeatureDraftPatchReply> suggestFeatureChange_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.SuggestFeatureChangeRequest> $request,
  ) async {
    return suggestFeatureChange($call, await $request);
  }

  $async.Future<$0.FeatureDraftPatchReply> suggestFeatureChange(
    $grpc.ServiceCall call,
    $0.SuggestFeatureChangeRequest request,
  );

  $async.Future<$0.FeatureReleaseReviewReply> verifyFeatureDraft_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.VerifyFeatureDraftRequest> $request,
  ) async {
    return verifyFeatureDraft($call, await $request);
  }

  $async.Future<$0.FeatureReleaseReviewReply> verifyFeatureDraft(
    $grpc.ServiceCall call,
    $0.VerifyFeatureDraftRequest request,
  );

  $async.Future<$0.FeatureAccessReviewReply> reviewFeatureAccess_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ReviewFeatureAccessRequest> $request,
  ) async {
    return reviewFeatureAccess($call, await $request);
  }

  $async.Future<$0.FeatureAccessReviewReply> reviewFeatureAccess(
    $grpc.ServiceCall call,
    $0.ReviewFeatureAccessRequest request,
  );

  $async.Future<$0.FeatureInstallReply> installFeatureVersion_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.InstallFeatureVersionRequest> $request,
  ) async {
    return installFeatureVersion($call, await $request);
  }

  $async.Future<$0.FeatureInstallReply> installFeatureVersion(
    $grpc.ServiceCall call,
    $0.InstallFeatureVersionRequest request,
  );

  $async.Future<$0.ResumeOriginatingRequestReply> resumeOriginatingRequest_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ResumeOriginatingRequestRequest> $request,
  ) async {
    return resumeOriginatingRequest($call, await $request);
  }

  $async.Future<$0.ResumeOriginatingRequestReply> resumeOriginatingRequest(
    $grpc.ServiceCall call,
    $0.ResumeOriginatingRequestRequest request,
  );

  $async.Future<$0.ListFeaturesReply> listFeatures_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ListFeaturesRequest> $request,
  ) async {
    return listFeatures($call, await $request);
  }

  $async.Future<$0.ListFeaturesReply> listFeatures(
    $grpc.ServiceCall call,
    $0.ListFeaturesRequest request,
  );

  $async.Future<$0.FeatureReply> getFeature_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetFeatureRequest> $request,
  ) async {
    return getFeature($call, await $request);
  }

  $async.Future<$0.FeatureReply> getFeature(
    $grpc.ServiceCall call,
    $0.GetFeatureRequest request,
  );

  $async.Future<$0.FeatureReleaseSourceReply> getFeatureReleaseSource_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetFeatureReleaseSourceRequest> $request,
  ) async {
    return getFeatureReleaseSource($call, await $request);
  }

  $async.Future<$0.FeatureReleaseSourceReply> getFeatureReleaseSource(
    $grpc.ServiceCall call,
    $0.GetFeatureReleaseSourceRequest request,
  );

  $async.Future<$0.FeatureReply> rollbackFeatureVersion_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.RollbackFeatureVersionRequest> $request,
  ) async {
    return rollbackFeatureVersion($call, await $request);
  }

  $async.Future<$0.FeatureReply> rollbackFeatureVersion(
    $grpc.ServiceCall call,
    $0.RollbackFeatureVersionRequest request,
  );

  $async.Future<$0.ListConnectionsReply> listConnections_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ListConnectionsRequest> $request,
  ) async {
    return listConnections($call, await $request);
  }

  $async.Future<$0.ListConnectionsReply> listConnections(
    $grpc.ServiceCall call,
    $0.ListConnectionsRequest request,
  );

  $async.Future<$0.ConnectionReply> getConnection_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetConnectionRequest> $request,
  ) async {
    return getConnection($call, await $request);
  }

  $async.Future<$0.ConnectionReply> getConnection(
    $grpc.ServiceCall call,
    $0.GetConnectionRequest request,
  );

  $async.Future<$0.ListActivityReply> listActivity_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ListActivityRequest> $request,
  ) async {
    return listActivity($call, await $request);
  }

  $async.Future<$0.ListActivityReply> listActivity(
    $grpc.ServiceCall call,
    $0.ListActivityRequest request,
  );

  $async.Future<$0.RunReply> getRun_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetRunRequest> $request,
  ) async {
    return getRun($call, await $request);
  }

  $async.Future<$0.RunReply> getRun(
    $grpc.ServiceCall call,
    $0.GetRunRequest request,
  );

  $async.Future<$0.ListMemoryItemsReply> listMemoryItems_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.ListMemoryItemsRequest> $request,
  ) async {
    return listMemoryItems($call, await $request);
  }

  $async.Future<$0.ListMemoryItemsReply> listMemoryItems(
    $grpc.ServiceCall call,
    $0.ListMemoryItemsRequest request,
  );

  $async.Future<$0.MemoryItemReply> getMemoryItem_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetMemoryItemRequest> $request,
  ) async {
    return getMemoryItem($call, await $request);
  }

  $async.Future<$0.MemoryItemReply> getMemoryItem(
    $grpc.ServiceCall call,
    $0.GetMemoryItemRequest request,
  );

  $async.Future<$0.HomeSummaryReply> getHomeSummary_Pre(
    $grpc.ServiceCall $call,
    $async.Future<$0.GetHomeSummaryRequest> $request,
  ) async {
    return getHomeSummary($call, await $request);
  }

  $async.Future<$0.HomeSummaryReply> getHomeSummary(
    $grpc.ServiceCall call,
    $0.GetHomeSummaryRequest request,
  );
}
