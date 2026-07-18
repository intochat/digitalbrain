import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/telemetry/telemetry.dart';
import 'package:ino_flutter/ui/components/bar_chart_card.dart';
import 'package:ino_flutter/voice/audio_transport.dart';
import 'package:ino_flutter/voice/audio_recorder.dart';

sealed class InoBlocEvent {}

class SendMessage extends InoBlocEvent {
  SendMessage(this.message);
  final String message;
}

/// Dispatched when a chip rendered from an AskClarification RFW payload is
/// tapped. The bloc fires the typed kernel synapse via the gateway's
/// FireSynapse RPC; the gateway looks up the user's active correlation_id
/// (stored when the Chat() turn that produced the AskClarification ran) and
/// pins the fire to the conversation-bearing grain activation. The fire's
/// response — typically the next AskClarification or the final TripItinerary
/// — comes back as a fresh chat message (rendered as either an RFW chip row
/// or the itinerary card).
class ProvideClarificationTapped extends InoBlocEvent {
  ProvideClarificationTapped(this.field, this.value);
  final String field;
  final String value;
}

class FetchTelemetry extends InoBlocEvent {
  FetchTelemetry({required this.query, this.userPrompt, this.limit = 10});
  final String query;
  final String? userPrompt; // original natural-language prompt shown in the chat
  final int limit;
}

class _TelemetryReceived extends InoBlocEvent {
  _TelemetryReceived(this.chartType, this.title, this.summary, this.entries);
  final String chartType;
  final String title;
  final String summary;
  final List<BarChartEntry> entries;
}

class StartRecording extends InoBlocEvent {}

class StopRecording extends InoBlocEvent {}

class _MessageReceived extends InoBlocEvent {
  _MessageReceived(
    this.reply, {
    this.rfwDescription,
    this.rfwData,
    this.contentType,
    this.correlationId,
    this.isSkeleton = false,
  });
  final String reply;
  final List<int>? rfwDescription;
  final List<int>? rfwData;
  final String? contentType;
  final String? correlationId;
  final bool isSkeleton;
}

/// Slice 4 — fired when a RemoteWidget event dispatches in a chat bubble's
/// RFW tree (e.g. <c>flight.selected { flightId: 'FL-002' }</c>). The bloc
/// calls <c>InoGrpcClient.rfwEvent</c>, which round-trips through the
/// gateway's <c>RfwEvent</c> RPC into the originating plan grain. The
/// response carries the next plan step's RFW payload inline; we surface it
/// as a fresh <c>_MessageReceived</c> so it appears as the next chat
/// message.
class RfwEventEmitted extends InoBlocEvent {
  RfwEventEmitted({
    required this.correlationId,
    required this.eventName,
    required this.args,
  });
  final String correlationId;
  final String eventName;
  final Map<String, String> args;
}

class _MessageFailed extends InoBlocEvent {
  _MessageFailed(this.error);
  final String error;
}

class _TranscriptReceived extends InoBlocEvent {
  _TranscriptReceived(this.text);
  final String text;
}

class _TranscriptFailed extends InoBlocEvent {
  _TranscriptFailed(this.error);
  final String error;
}

class ChatMessage {
  const ChatMessage({
    required this.text,
    required this.isUser,
    this.resultType,
    this.resultData,
    this.rfwDescription,
    this.rfwData,
    this.contentType,
    this.correlationId,
    this.telemetryTitle,
    this.telemetrySummary,
    this.telemetryEntries,
    this.isSkeleton = false,
  });
  final String text;
  final bool isUser;
  final String? resultType;
  final List<Map<String, dynamic>>? resultData;
  final List<int>? rfwDescription;
  final List<int>? rfwData;
  final String? contentType;
  // Slice 4 — conversation handle the gateway stamped on the response that
  // carried this RFW payload. RemoteWidget event callbacks include it in the
  // RfwEvent RPC so the gateway can resolve the originating plan grain.
  final String? correlationId;
  final String? telemetryTitle;
  final String? telemetrySummary;
  final List<BarChartEntry>? telemetryEntries;
  // True while this message is a placeholder rendered from the gateway's
  // skeleton frame. The FlightCard widget inspects the card data (empty
  // airline/from/to fields) to render shimmer; this flag tells the bubble
  // layer whether to animate the swap when the final frame arrives.
  final bool isSkeleton;
  bool get hasRfw => rfwDescription != null && rfwData != null && rfwDescription!.isNotEmpty;
  bool get hasTelemetry => telemetryEntries != null;
}

class InoBlocState {
  const InoBlocState({
    this.messages = const [],
    this.isLoading = false,
    this.isRecording = false,
  });

  final List<ChatMessage> messages;
  final bool isLoading;
  final bool isRecording;

  InoBlocState copyWith({
    List<ChatMessage>? messages,
    bool? isLoading,
    bool? isRecording,
  }) {
    return InoBlocState(
      messages: messages ?? this.messages,
      isLoading: isLoading ?? this.isLoading,
      isRecording: isRecording ?? this.isRecording,
    );
  }
}

class InoBloc extends Bloc<InoBlocEvent, InoBlocState> {
  InoBloc({
    required InoGrpcClient client,
    required AudioTransport audioTransport,
    required AudioRecorderService recorder,
  })  : _client = client,
        _audioTransport = audioTransport,
        _recorder = recorder,
        super(const InoBlocState()) {
    on<SendMessage>(_onSendMessage);
    on<ProvideClarificationTapped>(_onProvideClarificationTapped);
    on<RfwEventEmitted>(_onRfwEventEmitted);
    on<FetchTelemetry>(_onFetchTelemetry);
    on<StartRecording>(_onStartRecording);
    on<StopRecording>(_onStopRecording);
    on<_MessageReceived>(_onMessageReceived);
    on<_MessageFailed>(_onMessageFailed);
    on<_TelemetryReceived>(_onTelemetryReceived);
    on<_TranscriptReceived>(_onTranscriptReceived);
    on<_TranscriptFailed>(_onTranscriptFailed);
  }

  final InoGrpcClient _client;
  final AudioTransport _audioTransport;
  final AudioRecorderService _recorder;

  InoGrpcClient get grpcClient => _client;
  StreamSubscription<Uint8List>? _audioSubscription;
  final List<Uint8List> _audioBuffer = [];

  Future<void> _onSendMessage(
    SendMessage event,
    Emitter<InoBlocState> emit,
  ) async {
    final userMessage = ChatMessage(text: event.message, isUser: true);
    emit(state.copyWith(
      messages: [...state.messages, userMessage],
      isLoading: true,
    ));

    if (InoTelemetry.isInitialized) {
      InoTelemetry.instance.chatMessages.add(1, attributes: {
        'direction': 'outgoing',
      });
    }

    try {
      // Chat is now a server-streaming RPC. The gateway may emit a skeleton
      // frame first (is_skeleton=true) for RFW routes so the UI can paint
      // placeholder cards immediately, then a final frame with populated
      // data. Each frame dispatches _MessageReceived — the handler appends
      // the skeleton message and replaces it in-place when the final frame
      // lands.
      await for (final response in _client.chat(event.message)) {
        final rfwDesc = response.rfwDescription.isEmpty ? null : response.rfwDescription;
        final rfwData = response.rfwData.isEmpty ? null : response.rfwData;
        final contentType = response.contentType.isEmpty ? null : response.contentType;
        final correlationId = response.correlationId.isEmpty ? null : response.correlationId;
        add(_MessageReceived(
          response.reply,
          rfwDescription: rfwDesc,
          rfwData: rfwData,
          contentType: contentType,
          correlationId: correlationId,
          isSkeleton: response.isSkeleton,
        ));
      }
    } catch (e) {
      add(_MessageFailed(e.toString()));
    }
  }

  Future<void> _onProvideClarificationTapped(
    ProvideClarificationTapped event,
    Emitter<InoBlocState> emit,
  ) async {
    // Render the chip choice as a "user message" in the chat thread so the
    // conversation stays coherent — same shape as a typed reply.
    final userMessage = ChatMessage(text: event.value, isUser: true);
    emit(state.copyWith(
      messages: [...state.messages, userMessage],
      isLoading: true,
    ));

    if (InoTelemetry.isInitialized) {
      InoTelemetry.instance.chatMessages.add(1, attributes: {
        'direction': 'outgoing',
        'kind': 'clarification',
      });
    }

    try {
      final response = await _client.fireSynapse(
        'ino.core.provide-clarification',
        {'field': event.field, 'value': event.value},
      );
      final rfwDesc = response.rfwDescription.isEmpty ? null : response.rfwDescription;
      final rfwData = response.rfwData.isEmpty ? null : response.rfwData;
      final contentType = response.contentType.isEmpty ? null : response.contentType;
      final correlationId = response.correlationId.isEmpty ? null : response.correlationId;
      add(_MessageReceived(
        response.reply,
        rfwDescription: rfwDesc,
        rfwData: rfwData,
        contentType: contentType,
        correlationId: correlationId,
      ));
    } catch (e) {
      add(_MessageFailed(e.toString()));
    }
  }

  Future<void> _onRfwEventEmitted(
    RfwEventEmitted event,
    Emitter<InoBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));

    if (InoTelemetry.isInitialized) {
      InoTelemetry.instance.chatMessages.add(1, attributes: {
        'direction': 'outgoing',
        'kind': 'rfw_event',
      });
    }

    try {
      final response = await _client.rfwEvent(
        correlationId: event.correlationId,
        eventName: event.eventName,
        args: event.args,
      );
      final rfwDesc = response.rfwDescription.isEmpty ? null : response.rfwDescription;
      final rfwData = response.rfwData.isEmpty ? null : response.rfwData;
      final contentType = response.contentType.isEmpty ? null : response.contentType;
      final correlationId = response.correlationId.isEmpty ? null : response.correlationId;
      add(_MessageReceived(
        response.reply,
        rfwDescription: rfwDesc,
        rfwData: rfwData,
        contentType: contentType,
        correlationId: correlationId,
      ));
    } catch (e) {
      add(_MessageFailed(e.toString()));
    }
  }

  Future<void> _onFetchTelemetry(
    FetchTelemetry event,
    Emitter<InoBlocState> emit,
  ) async {
    final messagesWithUserPrompt = event.userPrompt != null
        ? [
            ...state.messages,
            ChatMessage(text: event.userPrompt!, isUser: true),
          ]
        : state.messages;

    emit(state.copyWith(
      messages: messagesWithUserPrompt,
      isLoading: true,
    ));

    try {
      final response = await _client.getTelemetry(
        query: event.query,
        limit: event.limit,
      );
      final entries = response.entries
          .map((e) => BarChartEntry(e.label, e.value))
          .toList();
      add(_TelemetryReceived(
        response.chartType,
        response.title,
        response.summary,
        entries,
      ));
    } catch (e) {
      add(_MessageFailed(e.toString()));
    }
  }

  void _onTelemetryReceived(
    _TelemetryReceived event,
    Emitter<InoBlocState> emit,
  ) {
    final reply = ChatMessage(
      text: event.summary,
      isUser: false,
      telemetryTitle: event.title,
      telemetrySummary: event.summary,
      telemetryEntries: event.entries,
    );
    emit(state.copyWith(
      messages: [...state.messages, reply],
      isLoading: false,
    ));
  }

  Future<void> _onStartRecording(
    StartRecording event,
    Emitter<InoBlocState> emit,
  ) async {
    final permitted = await _recorder.hasPermission();
    if (!permitted) {
      add(_TranscriptFailed('Microphone permission denied'));
      return;
    }

    _audioBuffer.clear();
    final stream = await _recorder.startRecording();
    _audioSubscription = stream.listen(
      (chunk) => _audioBuffer.add(chunk),
      onError: (Object e) => add(_TranscriptFailed(e.toString())),
    );

    emit(state.copyWith(isRecording: true));
  }

  Future<void> _onStopRecording(
    StopRecording event,
    Emitter<InoBlocState> emit,
  ) async {
    await _audioSubscription?.cancel();
    _audioSubscription = null;
    await _recorder.stopRecording();

    emit(state.copyWith(isRecording: false, isLoading: true));

    try {
      final transcript = await _audioTransport.transcribe(
        Stream.fromIterable(_audioBuffer),
      );
      add(_TranscriptReceived(transcript));
    } catch (e) {
      add(_TranscriptFailed(e.toString()));
    }
  }

  void _onTranscriptReceived(
    _TranscriptReceived event,
    Emitter<InoBlocState> emit,
  ) {
    emit(state.copyWith(isLoading: false));
    add(SendMessage(event.text));
  }

  void _onTranscriptFailed(
    _TranscriptFailed event,
    Emitter<InoBlocState> emit,
  ) {
    final errorMessage = ChatMessage(
      text: 'Voice error: ${event.error}',
      isUser: false,
    );
    emit(state.copyWith(
      messages: [...state.messages, errorMessage],
      isLoading: false,
      isRecording: false,
    ));
  }

  void _onMessageReceived(
    _MessageReceived event,
    Emitter<InoBlocState> emit,
  ) {
    String? resultType;
    List<Map<String, dynamic>>? resultData;
    var displayText = event.reply;

    try {
      final json = jsonDecode(event.reply);
      if (json is Map<String, dynamic> && json.containsKey('type')) {
        resultType = json['type'] as String;
        final dataKey = resultType.replaceAll('_results', 's');
        if (json[dataKey] is List) {
          resultData = (json[dataKey] as List).cast<Map<String, dynamic>>();
          displayText = 'Found ${resultData.length} results';
        }
      }
    } catch (_) {
      // Not JSON -- plain text response
    }

    if (InoTelemetry.isInitialized) {
      InoTelemetry.instance.chatMessages.add(1, attributes: {
        'direction': 'incoming',
      });
    }

    final reply = ChatMessage(
      text: displayText,
      isUser: false,
      resultType: resultType,
      resultData: resultData,
      rfwDescription: event.rfwDescription,
      rfwData: event.rfwData,
      contentType: event.contentType,
      correlationId: event.correlationId,
      isSkeleton: event.isSkeleton,
    );

    // If the previous bot message was a skeleton for the same request, swap
    // it in-place so the Flutter tree rebuilds the existing card with real
    // data (letting AnimatedSwitcher inside FlightCard drive the transition)
    // instead of popping a fresh card below the skeleton.
    final messages = state.messages;
    final shouldReplaceLast = messages.isNotEmpty &&
        !messages.last.isUser &&
        messages.last.isSkeleton;
    final nextMessages = shouldReplaceLast
        ? [...messages.sublist(0, messages.length - 1), reply]
        : [...messages, reply];

    emit(state.copyWith(
      messages: nextMessages,
      // Keep the loading indicator visible during the skeleton phase; the
      // real response clears it.
      isLoading: event.isSkeleton,
    ));
  }

  void _onMessageFailed(
    _MessageFailed event,
    Emitter<InoBlocState> emit,
  ) {
    final errorMessage = ChatMessage(
      text: 'Connection error: ${event.error}',
      isUser: false,
    );
    emit(state.copyWith(
      messages: [...state.messages, errorMessage],
      isLoading: false,
    ));
  }

  @override
  Future<void> close() {
    _audioSubscription?.cancel();
    return super.close();
  }
}
