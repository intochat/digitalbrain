// This is a generated file - do not edit.
//
// Generated from brainregistry.proto.

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

import 'brainregistry.pb.dart' as $0;

export 'brainregistry.pb.dart';

@$pb.GrpcServiceName('digitalbrain.BrainRegistry')
class BrainRegistryClient extends $grpc.Client {
  /// The hostname for this service.
  static const $core.String defaultHost = '';

  /// OAuth scopes needed for the client.
  static const $core.List<$core.String> oauthScopes = [
    '',
  ];

  BrainRegistryClient(super.channel, {super.options, super.interceptors});

  $grpc.ResponseFuture<$0.BrainsResult> listBrains(
    $0.ListBrainsRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$listBrains, request, options: options);
  }

  $grpc.ResponseStream<$0.BrainActivityDelta> watchActivity(
    $0.WatchActivityRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(
        _$watchActivity, $async.Stream.fromIterable([request]),
        options: options);
  }

  $grpc.ResponseFuture<$0.BrainCreated> createBrain(
    $0.CreateBrainCommand request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$createBrain, request, options: options);
  }

  $grpc.ResponseFuture<$0.BrainRenamed> renameBrain(
    $0.RenameBrainCommand request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$renameBrain, request, options: options);
  }

  $grpc.ResponseFuture<$0.BrainDeleted> deleteBrain(
    $0.DeleteBrainCommand request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$deleteBrain, request, options: options);
  }

  // method descriptors

  static final _$listBrains =
      $grpc.ClientMethod<$0.ListBrainsRequest, $0.BrainsResult>(
          '/digitalbrain.BrainRegistry/ListBrains',
          ($0.ListBrainsRequest value) => value.writeToBuffer(),
          $0.BrainsResult.fromBuffer);
  static final _$watchActivity =
      $grpc.ClientMethod<$0.WatchActivityRequest, $0.BrainActivityDelta>(
          '/digitalbrain.BrainRegistry/WatchActivity',
          ($0.WatchActivityRequest value) => value.writeToBuffer(),
          $0.BrainActivityDelta.fromBuffer);
  static final _$createBrain =
      $grpc.ClientMethod<$0.CreateBrainCommand, $0.BrainCreated>(
          '/digitalbrain.BrainRegistry/CreateBrain',
          ($0.CreateBrainCommand value) => value.writeToBuffer(),
          $0.BrainCreated.fromBuffer);
  static final _$renameBrain =
      $grpc.ClientMethod<$0.RenameBrainCommand, $0.BrainRenamed>(
          '/digitalbrain.BrainRegistry/RenameBrain',
          ($0.RenameBrainCommand value) => value.writeToBuffer(),
          $0.BrainRenamed.fromBuffer);
  static final _$deleteBrain =
      $grpc.ClientMethod<$0.DeleteBrainCommand, $0.BrainDeleted>(
          '/digitalbrain.BrainRegistry/DeleteBrain',
          ($0.DeleteBrainCommand value) => value.writeToBuffer(),
          $0.BrainDeleted.fromBuffer);
}

@$pb.GrpcServiceName('digitalbrain.BrainRegistry')
abstract class BrainRegistryServiceBase extends $grpc.Service {
  $core.String get $name => 'digitalbrain.BrainRegistry';

  BrainRegistryServiceBase() {
    $addMethod($grpc.ServiceMethod<$0.ListBrainsRequest, $0.BrainsResult>(
        'ListBrains',
        listBrains_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.ListBrainsRequest.fromBuffer(value),
        ($0.BrainsResult value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.WatchActivityRequest, $0.BrainActivityDelta>(
            'WatchActivity',
            watchActivity_Pre,
            false,
            true,
            ($core.List<$core.int> value) =>
                $0.WatchActivityRequest.fromBuffer(value),
            ($0.BrainActivityDelta value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.CreateBrainCommand, $0.BrainCreated>(
        'CreateBrain',
        createBrain_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.CreateBrainCommand.fromBuffer(value),
        ($0.BrainCreated value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.RenameBrainCommand, $0.BrainRenamed>(
        'RenameBrain',
        renameBrain_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.RenameBrainCommand.fromBuffer(value),
        ($0.BrainRenamed value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.DeleteBrainCommand, $0.BrainDeleted>(
        'DeleteBrain',
        deleteBrain_Pre,
        false,
        false,
        ($core.List<$core.int> value) =>
            $0.DeleteBrainCommand.fromBuffer(value),
        ($0.BrainDeleted value) => value.writeToBuffer()));
  }

  $async.Future<$0.BrainsResult> listBrains_Pre($grpc.ServiceCall $call,
      $async.Future<$0.ListBrainsRequest> $request) async {
    return listBrains($call, await $request);
  }

  $async.Future<$0.BrainsResult> listBrains(
      $grpc.ServiceCall call, $0.ListBrainsRequest request);

  $async.Stream<$0.BrainActivityDelta> watchActivity_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.WatchActivityRequest> $request) async* {
    yield* watchActivity($call, await $request);
  }

  $async.Stream<$0.BrainActivityDelta> watchActivity(
      $grpc.ServiceCall call, $0.WatchActivityRequest request);

  $async.Future<$0.BrainCreated> createBrain_Pre($grpc.ServiceCall $call,
      $async.Future<$0.CreateBrainCommand> $request) async {
    return createBrain($call, await $request);
  }

  $async.Future<$0.BrainCreated> createBrain(
      $grpc.ServiceCall call, $0.CreateBrainCommand request);

  $async.Future<$0.BrainRenamed> renameBrain_Pre($grpc.ServiceCall $call,
      $async.Future<$0.RenameBrainCommand> $request) async {
    return renameBrain($call, await $request);
  }

  $async.Future<$0.BrainRenamed> renameBrain(
      $grpc.ServiceCall call, $0.RenameBrainCommand request);

  $async.Future<$0.BrainDeleted> deleteBrain_Pre($grpc.ServiceCall $call,
      $async.Future<$0.DeleteBrainCommand> $request) async {
    return deleteBrain($call, await $request);
  }

  $async.Future<$0.BrainDeleted> deleteBrain(
      $grpc.ServiceCall call, $0.DeleteBrainCommand request);
}
