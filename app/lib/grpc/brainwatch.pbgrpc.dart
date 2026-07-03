// This is a generated file - do not edit.
//
// Generated from brainwatch.proto.

// @dart = 3.3

// ignore_for_file: annotate_overrides, camel_case_types, comment_references
// ignore_for_file: constant_identifier_names
// ignore_for_file: curly_braces_in_flow_control_structures
// ignore_for_file: deprecated_member_use_from_same_package, library_prefixes
// ignore_for_file: non_constant_identifier_names, prefer_relative_imports

import 'dart:async' as $async;
import 'dart:core' as $core;

import 'package:grpc/service_api.dart' as $grpc;
import 'package:protobuf/protobuf.dart' as $pb;

import 'brainwatch.pb.dart' as $0;

export 'brainwatch.pb.dart';

@$pb.GrpcServiceName('digitalbrain.BrainWatch')
class BrainWatchClient extends $grpc.Client {
  /// The hostname for this service.
  static const $core.String defaultHost = '';

  /// OAuth scopes needed for the client.
  static const $core.List<$core.String> oauthScopes = [
    '',
  ];

  BrainWatchClient(super.channel, {super.options, super.interceptors});

  $grpc.ResponseFuture<$0.SnapshotResponse> snapshot(
    $0.SnapshotRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$snapshot, request, options: options);
  }

  $grpc.ResponseFuture<$0.WatchSinceResponse> watchSince(
    $0.WatchSinceRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$watchSince, request, options: options);
  }

  $grpc.ResponseFuture<$0.NeuronDetailResponse> getNeuronDetail(
    $0.NeuronDetailRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getNeuronDetail, request, options: options);
  }

  $grpc.ResponseFuture<$0.SynapseDetailResponse> getSynapseDetail(
    $0.SynapseDetailRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getSynapseDetail, request, options: options);
  }

  $grpc.ResponseFuture<$0.NeuronFeatureResponse> getNeuronFeature(
    $0.NeuronFeatureRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getNeuronFeature, request, options: options);
  }

  $grpc.ResponseStream<$0.SynapseEdge> watchSynapses(
    $0.WatchSynapsesRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(
        _$watchSynapses, $async.Stream.fromIterable([request]),
        options: options);
  }

  // method descriptors

  static final _$snapshot =
      $grpc.ClientMethod<$0.SnapshotRequest, $0.SnapshotResponse>(
          '/digitalbrain.BrainWatch/Snapshot',
          ($0.SnapshotRequest value) => value.writeToBuffer(),
          $0.SnapshotResponse.fromBuffer);
  static final _$watchSince =
      $grpc.ClientMethod<$0.WatchSinceRequest, $0.WatchSinceResponse>(
          '/digitalbrain.BrainWatch/WatchSince',
          ($0.WatchSinceRequest value) => value.writeToBuffer(),
          $0.WatchSinceResponse.fromBuffer);
  static final _$getNeuronDetail =
      $grpc.ClientMethod<$0.NeuronDetailRequest, $0.NeuronDetailResponse>(
          '/digitalbrain.BrainWatch/GetNeuronDetail',
          ($0.NeuronDetailRequest value) => value.writeToBuffer(),
          $0.NeuronDetailResponse.fromBuffer);
  static final _$getSynapseDetail =
      $grpc.ClientMethod<$0.SynapseDetailRequest, $0.SynapseDetailResponse>(
          '/digitalbrain.BrainWatch/GetSynapseDetail',
          ($0.SynapseDetailRequest value) => value.writeToBuffer(),
          $0.SynapseDetailResponse.fromBuffer);
  static final _$getNeuronFeature =
      $grpc.ClientMethod<$0.NeuronFeatureRequest, $0.NeuronFeatureResponse>(
          '/digitalbrain.BrainWatch/GetNeuronFeature',
          ($0.NeuronFeatureRequest value) => value.writeToBuffer(),
          $0.NeuronFeatureResponse.fromBuffer);
  static final _$watchSynapses =
      $grpc.ClientMethod<$0.WatchSynapsesRequest, $0.SynapseEdge>(
          '/digitalbrain.BrainWatch/WatchSynapses',
          ($0.WatchSynapsesRequest value) => value.writeToBuffer(),
          $0.SynapseEdge.fromBuffer);
}

@$pb.GrpcServiceName('digitalbrain.BrainWatch')
abstract class BrainWatchServiceBase extends $grpc.Service {
  $core.String get $name => 'digitalbrain.BrainWatch';

  BrainWatchServiceBase() {
    $addMethod($grpc.ServiceMethod<$0.SnapshotRequest, $0.SnapshotResponse>(
        'Snapshot',
        snapshot_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.SnapshotRequest.fromBuffer(value),
        ($0.SnapshotResponse value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.WatchSinceRequest, $0.WatchSinceResponse>(
        'WatchSince',
        watchSince_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.WatchSinceRequest.fromBuffer(value),
        ($0.WatchSinceResponse value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.NeuronDetailRequest, $0.NeuronDetailResponse>(
            'GetNeuronDetail',
            getNeuronDetail_Pre,
            false,
            false,
            ($core.List<$core.int> value) =>
                $0.NeuronDetailRequest.fromBuffer(value),
            ($0.NeuronDetailResponse value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.SynapseDetailRequest, $0.SynapseDetailResponse>(
            'GetSynapseDetail',
            getSynapseDetail_Pre,
            false,
            false,
            ($core.List<$core.int> value) =>
                $0.SynapseDetailRequest.fromBuffer(value),
            ($0.SynapseDetailResponse value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.NeuronFeatureRequest, $0.NeuronFeatureResponse>(
            'GetNeuronFeature',
            getNeuronFeature_Pre,
            false,
            false,
            ($core.List<$core.int> value) =>
                $0.NeuronFeatureRequest.fromBuffer(value),
            ($0.NeuronFeatureResponse value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.WatchSynapsesRequest, $0.SynapseEdge>(
        'WatchSynapses',
        watchSynapses_Pre,
        false,
        true,
        ($core.List<$core.int> value) =>
            $0.WatchSynapsesRequest.fromBuffer(value),
        ($0.SynapseEdge value) => value.writeToBuffer()));
  }

  $async.Future<$0.SnapshotResponse> snapshot_Pre($grpc.ServiceCall $call,
      $async.Future<$0.SnapshotRequest> $request) async {
    return snapshot($call, await $request);
  }

  $async.Future<$0.SnapshotResponse> snapshot(
      $grpc.ServiceCall call, $0.SnapshotRequest request);

  $async.Future<$0.WatchSinceResponse> watchSince_Pre($grpc.ServiceCall $call,
      $async.Future<$0.WatchSinceRequest> $request) async {
    return watchSince($call, await $request);
  }

  $async.Future<$0.WatchSinceResponse> watchSince(
      $grpc.ServiceCall call, $0.WatchSinceRequest request);

  $async.Future<$0.NeuronDetailResponse> getNeuronDetail_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.NeuronDetailRequest> $request) async {
    return getNeuronDetail($call, await $request);
  }

  $async.Future<$0.NeuronDetailResponse> getNeuronDetail(
      $grpc.ServiceCall call, $0.NeuronDetailRequest request);

  $async.Future<$0.SynapseDetailResponse> getSynapseDetail_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.SynapseDetailRequest> $request) async {
    return getSynapseDetail($call, await $request);
  }

  $async.Future<$0.SynapseDetailResponse> getSynapseDetail(
      $grpc.ServiceCall call, $0.SynapseDetailRequest request);

  $async.Future<$0.NeuronFeatureResponse> getNeuronFeature_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.NeuronFeatureRequest> $request) async {
    return getNeuronFeature($call, await $request);
  }

  $async.Future<$0.NeuronFeatureResponse> getNeuronFeature(
      $grpc.ServiceCall call, $0.NeuronFeatureRequest request);

  $async.Stream<$0.SynapseEdge> watchSynapses_Pre($grpc.ServiceCall $call,
      $async.Future<$0.WatchSynapsesRequest> $request) async* {
    yield* watchSynapses($call, await $request);
  }

  $async.Stream<$0.SynapseEdge> watchSynapses(
      $grpc.ServiceCall call, $0.WatchSynapsesRequest request);
}
