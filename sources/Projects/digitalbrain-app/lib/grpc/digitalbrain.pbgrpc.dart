// This is a generated file - do not edit.
//
// Generated from digitalbrain.proto.

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

import 'digitalbrain.pb.dart' as $0;

export 'digitalbrain.pb.dart';

@$pb.GrpcServiceName('digitalbrain.DigitalBrainGateway')
class DigitalBrainGatewayClient extends $grpc.Client {
  /// The hostname for this service.
  static const $core.String defaultHost = '';

  /// OAuth scopes needed for the client.
  static const $core.List<$core.String> oauthScopes = [
    '',
  ];

  DigitalBrainGatewayClient(super.channel, {super.options, super.interceptors});

  $grpc.ResponseFuture<$0.SynapseEnvelope> send(
    $0.SynapseEnvelope request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$send, request, options: options);
  }

  $grpc.ResponseStream<$0.RfwCardEnvelope> watchHomeFeed(
    $0.WatchHomeFeedRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(
        _$watchHomeFeed, $async.Stream.fromIterable([request]),
        options: options);
  }

  $grpc.ResponseFuture<$0.TranscribeResponse> transcribe(
    $async.Stream<$0.TranscribeRequest> request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(_$transcribe, request, options: options).single;
  }

  $grpc.ResponseFuture<$0.AiHealthResponse> aiHealth(
    $0.AiHealthRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$aiHealth, request, options: options);
  }

  $grpc.ResponseFuture<$0.SubmitPromptReply> submitPrompt(
    $0.SubmitPromptRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$submitPrompt, request, options: options);
  }

  $grpc.ResponseFuture<$0.FlutterPerfAck> pushFlutterPerf(
    $async.Stream<$0.FlutterPerfSampleProto> request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(_$pushFlutterPerf, request, options: options)
        .single;
  }

  $grpc.ResponseStream<$0.VisualLoadHintProto> watchVisualLoadHint(
    $0.WatchVisualLoadHintRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createStreamingCall(
        _$watchVisualLoadHint, $async.Stream.fromIterable([request]),
        options: options);
  }

  $grpc.ResponseFuture<$0.GetLatestCardReply> getLatestCard(
    $0.GetLatestCardRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getLatestCard, request, options: options);
  }

  $grpc.ResponseFuture<$0.RfwLayoutReply> getRfwLayout(
    $0.RfwLayoutRequest request, {
    $grpc.CallOptions? options,
  }) {
    return $createUnaryCall(_$getRfwLayout, request, options: options);
  }

  // method descriptors

  static final _$send =
      $grpc.ClientMethod<$0.SynapseEnvelope, $0.SynapseEnvelope>(
          '/digitalbrain.DigitalBrainGateway/Send',
          ($0.SynapseEnvelope value) => value.writeToBuffer(),
          $0.SynapseEnvelope.fromBuffer);
  static final _$watchHomeFeed =
      $grpc.ClientMethod<$0.WatchHomeFeedRequest, $0.RfwCardEnvelope>(
          '/digitalbrain.DigitalBrainGateway/WatchHomeFeed',
          ($0.WatchHomeFeedRequest value) => value.writeToBuffer(),
          $0.RfwCardEnvelope.fromBuffer);
  static final _$transcribe =
      $grpc.ClientMethod<$0.TranscribeRequest, $0.TranscribeResponse>(
          '/digitalbrain.DigitalBrainGateway/Transcribe',
          ($0.TranscribeRequest value) => value.writeToBuffer(),
          $0.TranscribeResponse.fromBuffer);
  static final _$aiHealth =
      $grpc.ClientMethod<$0.AiHealthRequest, $0.AiHealthResponse>(
          '/digitalbrain.DigitalBrainGateway/AiHealth',
          ($0.AiHealthRequest value) => value.writeToBuffer(),
          $0.AiHealthResponse.fromBuffer);
  static final _$submitPrompt =
      $grpc.ClientMethod<$0.SubmitPromptRequest, $0.SubmitPromptReply>(
          '/digitalbrain.DigitalBrainGateway/SubmitPrompt',
          ($0.SubmitPromptRequest value) => value.writeToBuffer(),
          $0.SubmitPromptReply.fromBuffer);
  static final _$pushFlutterPerf =
      $grpc.ClientMethod<$0.FlutterPerfSampleProto, $0.FlutterPerfAck>(
          '/digitalbrain.DigitalBrainGateway/PushFlutterPerf',
          ($0.FlutterPerfSampleProto value) => value.writeToBuffer(),
          $0.FlutterPerfAck.fromBuffer);
  static final _$watchVisualLoadHint =
      $grpc.ClientMethod<$0.WatchVisualLoadHintRequest, $0.VisualLoadHintProto>(
          '/digitalbrain.DigitalBrainGateway/WatchVisualLoadHint',
          ($0.WatchVisualLoadHintRequest value) => value.writeToBuffer(),
          $0.VisualLoadHintProto.fromBuffer);
  static final _$getLatestCard =
      $grpc.ClientMethod<$0.GetLatestCardRequest, $0.GetLatestCardReply>(
          '/digitalbrain.DigitalBrainGateway/GetLatestCard',
          ($0.GetLatestCardRequest value) => value.writeToBuffer(),
          $0.GetLatestCardReply.fromBuffer);
  static final _$getRfwLayout =
      $grpc.ClientMethod<$0.RfwLayoutRequest, $0.RfwLayoutReply>(
          '/digitalbrain.DigitalBrainGateway/GetRfwLayout',
          ($0.RfwLayoutRequest value) => value.writeToBuffer(),
          $0.RfwLayoutReply.fromBuffer);
}

@$pb.GrpcServiceName('digitalbrain.DigitalBrainGateway')
abstract class DigitalBrainGatewayServiceBase extends $grpc.Service {
  $core.String get $name => 'digitalbrain.DigitalBrainGateway';

  DigitalBrainGatewayServiceBase() {
    $addMethod($grpc.ServiceMethod<$0.SynapseEnvelope, $0.SynapseEnvelope>(
        'Send',
        send_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.SynapseEnvelope.fromBuffer(value),
        ($0.SynapseEnvelope value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.WatchHomeFeedRequest, $0.RfwCardEnvelope>(
        'WatchHomeFeed',
        watchHomeFeed_Pre,
        false,
        true,
        ($core.List<$core.int> value) =>
            $0.WatchHomeFeedRequest.fromBuffer(value),
        ($0.RfwCardEnvelope value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.TranscribeRequest, $0.TranscribeResponse>(
        'Transcribe',
        transcribe,
        true,
        false,
        ($core.List<$core.int> value) => $0.TranscribeRequest.fromBuffer(value),
        ($0.TranscribeResponse value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.AiHealthRequest, $0.AiHealthResponse>(
        'AiHealth',
        aiHealth_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.AiHealthRequest.fromBuffer(value),
        ($0.AiHealthResponse value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.SubmitPromptRequest, $0.SubmitPromptReply>(
            'SubmitPrompt',
            submitPrompt_Pre,
            false,
            false,
            ($core.List<$core.int> value) =>
                $0.SubmitPromptRequest.fromBuffer(value),
            ($0.SubmitPromptReply value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.FlutterPerfSampleProto, $0.FlutterPerfAck>(
            'PushFlutterPerf',
            pushFlutterPerf,
            true,
            false,
            ($core.List<$core.int> value) =>
                $0.FlutterPerfSampleProto.fromBuffer(value),
            ($0.FlutterPerfAck value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.WatchVisualLoadHintRequest,
            $0.VisualLoadHintProto>(
        'WatchVisualLoadHint',
        watchVisualLoadHint_Pre,
        false,
        true,
        ($core.List<$core.int> value) =>
            $0.WatchVisualLoadHintRequest.fromBuffer(value),
        ($0.VisualLoadHintProto value) => value.writeToBuffer()));
    $addMethod(
        $grpc.ServiceMethod<$0.GetLatestCardRequest, $0.GetLatestCardReply>(
            'GetLatestCard',
            getLatestCard_Pre,
            false,
            false,
            ($core.List<$core.int> value) =>
                $0.GetLatestCardRequest.fromBuffer(value),
            ($0.GetLatestCardReply value) => value.writeToBuffer()));
    $addMethod($grpc.ServiceMethod<$0.RfwLayoutRequest, $0.RfwLayoutReply>(
        'GetRfwLayout',
        getRfwLayout_Pre,
        false,
        false,
        ($core.List<$core.int> value) => $0.RfwLayoutRequest.fromBuffer(value),
        ($0.RfwLayoutReply value) => value.writeToBuffer()));
  }

  $async.Future<$0.SynapseEnvelope> send_Pre($grpc.ServiceCall $call,
      $async.Future<$0.SynapseEnvelope> $request) async {
    return send($call, await $request);
  }

  $async.Future<$0.SynapseEnvelope> send(
      $grpc.ServiceCall call, $0.SynapseEnvelope request);

  $async.Stream<$0.RfwCardEnvelope> watchHomeFeed_Pre($grpc.ServiceCall $call,
      $async.Future<$0.WatchHomeFeedRequest> $request) async* {
    yield* watchHomeFeed($call, await $request);
  }

  $async.Stream<$0.RfwCardEnvelope> watchHomeFeed(
      $grpc.ServiceCall call, $0.WatchHomeFeedRequest request);

  $async.Future<$0.TranscribeResponse> transcribe(
      $grpc.ServiceCall call, $async.Stream<$0.TranscribeRequest> request);

  $async.Future<$0.AiHealthResponse> aiHealth_Pre($grpc.ServiceCall $call,
      $async.Future<$0.AiHealthRequest> $request) async {
    return aiHealth($call, await $request);
  }

  $async.Future<$0.AiHealthResponse> aiHealth(
      $grpc.ServiceCall call, $0.AiHealthRequest request);

  $async.Future<$0.SubmitPromptReply> submitPrompt_Pre($grpc.ServiceCall $call,
      $async.Future<$0.SubmitPromptRequest> $request) async {
    return submitPrompt($call, await $request);
  }

  $async.Future<$0.SubmitPromptReply> submitPrompt(
      $grpc.ServiceCall call, $0.SubmitPromptRequest request);

  $async.Future<$0.FlutterPerfAck> pushFlutterPerf(
      $grpc.ServiceCall call, $async.Stream<$0.FlutterPerfSampleProto> request);

  $async.Stream<$0.VisualLoadHintProto> watchVisualLoadHint_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.WatchVisualLoadHintRequest> $request) async* {
    yield* watchVisualLoadHint($call, await $request);
  }

  $async.Stream<$0.VisualLoadHintProto> watchVisualLoadHint(
      $grpc.ServiceCall call, $0.WatchVisualLoadHintRequest request);

  $async.Future<$0.GetLatestCardReply> getLatestCard_Pre(
      $grpc.ServiceCall $call,
      $async.Future<$0.GetLatestCardRequest> $request) async {
    return getLatestCard($call, await $request);
  }

  $async.Future<$0.GetLatestCardReply> getLatestCard(
      $grpc.ServiceCall call, $0.GetLatestCardRequest request);

  $async.Future<$0.RfwLayoutReply> getRfwLayout_Pre($grpc.ServiceCall $call,
      $async.Future<$0.RfwLayoutRequest> $request) async {
    return getRfwLayout($call, await $request);
  }

  $async.Future<$0.RfwLayoutReply> getRfwLayout(
      $grpc.ServiceCall call, $0.RfwLayoutRequest request);
}
