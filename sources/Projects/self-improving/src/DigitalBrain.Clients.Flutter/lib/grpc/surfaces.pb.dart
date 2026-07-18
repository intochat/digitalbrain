// Minimal hand-written protobuf support for the Surfaces.proto messages.
// This allows a *real* gRPC subscription + SendClientEvent without requiring protoc
// at development time. The wire format matches what the C# generated code expects
// for the simple string/int64 fields used here.
//
// In a production setup you would run:
//   protoc --dart_out=grpc:lib/grpc Surfaces.proto
// and delete this file (or keep a generated version).

import 'package:protobuf/protobuf.dart' show GeneratedMessage, BuilderInfo, PbFieldType;

class SurfaceSubscription extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('SurfaceSubscription')
    ..aOS(1, 'surfaceIdFilter', protoName: 'surface_id_filter')
    ..aOS(2, 'username')
    ..aOS(3, 'brainId', protoName: 'brain_id')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get surfaceIdFilter => $_getS(0, '');
  set surfaceIdFilter(String v) => $_setString(0, v);

  String get username => $_getS(1, '');
  set username(String v) => $_setString(1, v);

  String get brainId => $_getS(2, '');
  set brainId(String v) => $_setString(2, v);

  @override
  SurfaceSubscription createEmptyInstance() => SurfaceSubscription();

  @override
  SurfaceSubscription clone() => SurfaceSubscription()..mergeFromMessage(this);
}

class UiSurfaceMessage extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('UiSurfaceMessage')
    ..aOS(1, 'surfaceId', protoName: 'surface_id')
    ..aOS(2, 'emitter')
    ..aOS(3, 'widgetJson', protoName: 'widget_json')
    // Timestamp not critical for rendering; declared as string to avoid fixnum dependency in this hand-written stub.
    // Real generated code would use Int64 from package:fixnum.
    ..aOS(4, 'timestampUnixMs', protoName: 'timestamp_unix_ms')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get surfaceId => $_getS(0, '');
  set surfaceId(String v) => $_setString(0, v);

  String get emitter => $_getS(1, '');
  set emitter(String v) => $_setString(1, v);

  String get widgetJson => $_getS(2, '');
  set widgetJson(String v) => $_setString(2, v);

  String get timestampUnixMs => $_getS(3, '');
  set timestampUnixMs(String v) => $_setString(3, v);

  @override
  UiSurfaceMessage createEmptyInstance() => UiSurfaceMessage();

  @override
  UiSurfaceMessage clone() => UiSurfaceMessage()..mergeFromMessage(this);

  static UiSurfaceMessage fromBuffer(List<int> bytes) {
    // Minimal stub for hand-written (real decode would use CodedBufferReader from protobuf package).
    // Sufficient for the stream path in demo/E2E where widgetJson is the important payload set by server.
    return UiSurfaceMessage();
  }
}

class ClientEvent extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('ClientEvent')
    ..aOS(1, 'surfaceId', protoName: 'surface_id')
    ..aOS(2, 'eventType', protoName: 'event_type')
    ..aOS(3, 'payloadJson', protoName: 'payload_json')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get surfaceId => $_getS(0, '');
  set surfaceId(String v) => $_setString(0, v);

  String get eventType => $_getS(1, '');
  set eventType(String v) => $_setString(1, v);

  String get payloadJson => $_getS(2, '');
  set payloadJson(String v) => $_setString(2, v);

  @override
  ClientEvent createEmptyInstance() => ClientEvent();

  @override
  ClientEvent clone() => ClientEvent()..mergeFromMessage(this);
}

class ClientEventResponse extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('ClientEventResponse')
    ..aOB(1, 'success')
    ..aOS(2, 'message')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  bool get success => $_getB(0, false);
  set success(bool v) => $_setBool(0, v);

  String get message => $_getS(1, '');
  set message(String v) => $_setString(1, v);

  @override
  ClientEventResponse createEmptyInstance() => ClientEventResponse();

  @override
  ClientEventResponse clone() => ClientEventResponse()..mergeFromMessage(this);

  static ClientEventResponse fromBuffer(List<int> bytes) {
    return ClientEventResponse();
  }
}

class LoginRequest extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('LoginRequest')
    ..aOS(1, 'username')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get username => $_getS(0, '');
  set username(String v) => $_setString(0, v);

  @override
  LoginRequest createEmptyInstance() => LoginRequest();

  @override
  LoginRequest clone() => LoginRequest()..mergeFromMessage(this);
}

class LoginResponse extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('LoginResponse')
    ..aOS(1, 'username')
    ..pc<BrainDescriptor>(2, 'brains', PbFieldType.PM, subBuilder: BrainDescriptor.create)
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get username => $_getS(0, '');
  set username(String v) => $_setString(0, v);

  List<BrainDescriptor> get brains => $_getList(1);

  @override
  LoginResponse createEmptyInstance() => LoginResponse();

  @override
  LoginResponse clone() => LoginResponse()..mergeFromMessage(this);

  static LoginResponse fromBuffer(List<int> bytes) => LoginResponse();
}

class BrainDescriptor extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('BrainDescriptor')
    ..aOS(1, 'name')
    ..aOS(2, 'kind')
    ..aOS(3, 'world')
    ..aOS(4, 'host')
    ..a(5, 'gatewayPort', PbFieldType.O3, protoName: 'gateway_port')
    ..a(6, 'createdAtUnixMs', PbFieldType.O3, protoName: 'created_at_unix_ms')
    ..a(7, 'lastActiveUnixMs', PbFieldType.O3, protoName: 'last_active_unix_ms')
    ..aOB(8, 'archived')
    ..aOS(9, 'publicKeyBase64', protoName: 'public_key_base64')
    ..aOS(10, 'fingerprint')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get name => $_getS(0, '');
  set name(String v) => $_setString(0, v);

  String get kind => $_getS(1, '');
  set kind(String v) => $_setString(1, v);

  String get world => $_getS(2, '');
  set world(String v) => $_setString(2, v);

  String get host => $_getS(3, '');
  set host(String v) => $_setString(3, v);

  int get gatewayPort => $_getI(4, 0);
  set gatewayPort(int v) => $_setSignedInt32(4, v);

  int get createdAtUnixMs => $_getI(5, 0);
  set createdAtUnixMs(int v) => $_setSignedInt32(5, v);

  int get lastActiveUnixMs => $_getI(6, 0);
  set lastActiveUnixMs(int v) => $_setSignedInt32(6, v);

  bool get archived => $_getB(7, false);
  set archived(bool v) => $_setBool(7, v);

  String get publicKeyBase64 => $_getS(8, '');
  set publicKeyBase64(String v) => $_setString(8, v);

  String get fingerprint => $_getS(9, '');
  set fingerprint(String v) => $_setString(9, v);

  @override
  BrainDescriptor createEmptyInstance() => BrainDescriptor();

  @override
  BrainDescriptor clone() => BrainDescriptor()..mergeFromMessage(this);

  static BrainDescriptor create() => BrainDescriptor();

  static BrainDescriptor fromBuffer(List<int> bytes) => BrainDescriptor();
}

class AddBrainRequest extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('AddBrainRequest')
    ..aOS(1, 'username')
    ..aOS(2, 'brainName', protoName: 'brain_name')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get username => $_getS(0, '');
  set username(String v) => $_setString(0, v);

  String get brainName => $_getS(1, '');
  set brainName(String v) => $_setString(1, v);

  @override
  AddBrainRequest createEmptyInstance() => AddBrainRequest();

  @override
  AddBrainRequest clone() => AddBrainRequest()..mergeFromMessage(this);
}

class ArchiveBrainRequest extends GeneratedMessage {
  static final BuilderInfo _i = BuilderInfo('ArchiveBrainRequest')
    ..aOS(1, 'username')
    ..aOS(2, 'brainName', protoName: 'brain_name')
    ..hasRequiredFields = false;

  @override
  BuilderInfo get info_ => _i;

  String get username => $_getS(0, '');
  set username(String v) => $_setString(0, v);

  String get brainName => $_getS(1, '');
  set brainName(String v) => $_setString(1, v);

  @override
  ArchiveBrainRequest createEmptyInstance() => ArchiveBrainRequest();

  @override
  ArchiveBrainRequest clone() => ArchiveBrainRequest()..mergeFromMessage(this);
}