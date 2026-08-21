# Task 5 Report: Flutter native PersonaPlex voice

## Status and commit

Task 5 is implemented in commit `f1465d2b10d679618f5978345373379f6878dec2`
(`feat: add native PersonaPlex voice workspace`). The evidence report is kept in
a follow-up documentation-only commit so that it can cite the immutable
implementation commit exactly.

The implementation adds an independent Voice destination. It does not call the
existing chat voice upload route, Whisper/STT, `MapChatVoice`, or
`IChatClient`. A source scan of the new transport/controller/output files found
none of those names and found no raw-audio logging.

## Dependency decision

Current package documentation was checked through Context7 for
`/alnitak/flutter_soloud` and `/llfbandit/record` before selecting playback.

- Capture keeps the existing `record ^7.1.1`. Its current API supports
  `AudioEncoder.pcm16bits`, `startStream`, explicit `sampleRate`,
  `numChannels`, `streamBufferSize`, and an actual-config adjustment callback
  on Windows and web.
- Playback adds `flutter_soloud ^4.1.7`. It provides a real continuous push
  buffer on Windows and web through `setBufferStream`, explicit
  `BufferType.s16le`, sample rate and channel selection, and
  `addAudioDataStream`. The native 4.1.7 source explicitly initializes
  `SoLoud::MINIAUDIO`; it does not select the compiled NULL backend as an
  automatic production fallback.
- Core adds direct `web_socket_channel ^3.0.3` ownership for the dedicated
  PersonaPlex socket.

`SoLoudPcmAudioOutput` initializes at exactly 24,000 Hz, one channel, a
1,920-sample engine buffer, signed little-endian PCM16, and an 80 ms stream
buffering target. It starts one continuous stream, appends each decoded frame,
and releases the handle/source and engine on stop.

Web has a precise runtime constraint: `flutter_soloud` web output requires a
Flutter `--wasm` build or a legacy web host configured for cross-origin
isolation with COOP/COEP. The `--wasm` build compiles successfully. On a web
deployment that does not satisfy the package runtime requirement, audio init or
playback becomes the explicit `unavailable` UI state with this message:

> Continuous 24 kHz mono PCM16 playback is unavailable in this web host.
> flutter_soloud web audio requires a --wasm build or cross-origin-isolated
> COOP/COEP hosting.

There is no silent/no-op production output implementation.

## RED/GREEN proof

Development followed the requested test-first sequence. The meaningful
observed failures and their matching green runs were:

1. Protocol decoder RED: the focused core test did not compile because the
   PersonaPlex packet/client types did not exist. GREEN after adding the
   protocol: a 1,920-sample packet preserved its 64-bit sequence and all 3,840
   PCM bytes.
2. Same-origin client RED: URI conversion retained the base URI query/fragment.
   GREEN after constructing a fresh URI:
   `https://brain.example:7443/...` maps exactly to
   `wss://brain.example:7443/voice/personaplex`.
3. Controller cleanup RED: the focused controller test did not compile because
   capture/output/transport lifecycle types did not exist. GREEN after the
   controller implementation: repeated stop/dispose closes capture, playback,
   and socket exactly once.
4. Exact framing RED: expanded controller tests initially lacked framing,
   playback metrics, permission, and unavailable behavior. GREEN verifies
   arbitrary microphone chunks are reframed into ordered 3,840-byte payloads,
   response PCM is played, and adjusted capture format is unavailable.
5. Workspace cleanup RED: the Voice-to-Chat widget regression reported zero
   resource stops because shutdown awaited stream cancellation before invoking
   the resource stops. GREEN after starting subscription cancellation,
   recorder stop, output stop, and socket close together.
6. Peer-close RED: close raised `Bad state: peer closed` and did not close the
   channel when the stop control send failed. GREEN after making stop control
   best-effort while channel close remains mandatory.
7. Socket-send RED:
   `flutter test ... --plain-name "socket send failure enters error state and stops resources"`
   failed with `Bad state: socket closed` and left the controller active.
   GREEN after routing send failure through controller error shutdown.
8. Connect/close race RED: expected one channel close but observed two when
   closing during the ready handshake. GREEN after assigning sole close
   ownership to the close path.
9. Startup/stop races RED: stopping during a delayed permission check left the
   phase `connecting`, and stopping during output initialization created one
   late stream instead of zero. GREEN after shutdown guards and late-init
   deinitialization. Recorder stop also waits for any in-flight
   `record.startStream`, preventing late capture from escaping tab cleanup.

Final focused controller result: `+9: All tests passed!`. Final focused core
protocol/client coverage is included in the 32-test core suite.

## Protocol and transport behavior

- The header is exactly 16 bytes, little-endian: `int32 version = 1`,
  `int64 sequence`, `int32 sampleCount = 1920`.
- Every binary message is exactly 3,856 bytes: 16-byte header plus 3,840-byte
  mono PCM16 payload. Decode and encode reject any other size/version/sample
  count and non-positive sequence.
- Microphone input is exactly 24,000 Hz mono PCM16. Arbitrary recorder chunks
  are buffered and emitted only as 1,920-sample frames with sequence starting
  at one.
- The socket is the same-origin `/voice/personaplex` route using `ws`/`wss`,
  sends JSON `start`, accepts JSON status/error/stop and binary audio, then
  best-effort sends JSON `stop` before lifecycle-safe close.
- Capture begins only after the Kernel reports `ready`.

## UI behavior

- Voice is the second persistent `BrainWorkspace` destination on both rail and
  bottom navigation.
- With no Kernel base URI, the screen is visibly unavailable and Start is
  disabled.
- Start creates only the native PersonaPlex controller, shows connecting/model
  readiness, and then shows the active listening/speaking state.
- The screen renders microphone RMS, speaker RMS, response latency, and a Stop
  action.
- Permission denial, unsupported/adjusted microphone format, output
  unavailable, protocol/socket errors, stopped, loading, ready, and idle are
  distinct visible states. Permission/error/unavailable sessions can be
  retried by Start when an endpoint exists.
- Stop, switching away from Voice, disposing the screen, and switching during
  asynchronous startup all stop capture, playback, and socket. Cleanup is
  idempotent.

## Changed files

Core:

- `src/Modules/UI/Flutter/core/lib/src/personaplex_voice_protocol.dart`
- `src/Modules/UI/Flutter/core/lib/src/personaplex_voice_client.dart`
- `src/Modules/UI/Flutter/core/lib/digitalbrain_flutter.dart`
- `src/Modules/UI/Flutter/core/pubspec.yaml`
- `src/Modules/UI/Flutter/core/pubspec.lock`
- `src/Modules/UI/Flutter/core/test/personaplex_voice_protocol_test.dart`

Shell voice implementation:

- `src/Modules/UI/Flutter/shell/lib/voice/pcm_audio_output.dart`
- `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_controller.dart`
- `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_screen.dart`

Shell integration and dependencies:

- `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_app.dart`
- `src/Modules/UI/Flutter/shell/lib/chat/brain_workspace.dart`
- `src/Modules/UI/Flutter/shell/lib/chat/chat_contracts.dart`
- `src/Modules/UI/Flutter/shell/lib/chat/workspace_chrome.dart`
- `src/Modules/UI/Flutter/shell/lib/chat_screen.dart`
- `src/Modules/UI/Flutter/shell/lib/main.dart`
- `src/Modules/UI/Flutter/shell/pubspec.yaml`
- `src/Modules/UI/Flutter/shell/pubspec.lock`
- `src/Modules/UI/Flutter/shell/windows/flutter/generated_plugins.cmake`
- `src/Modules/UI/Flutter/shell/test/voice/personaplex_voice_controller_test.dart`
- `src/Modules/UI/Flutter/shell/test/workspace_test.dart`

## Final verification

- `dart format ...` on all Task 5 Dart files: success, 15 files formatted.
- Core `flutter analyze`: success, no issues.
- Shell `flutter analyze lib/voice test/voice`: success, no issues.
- Full shell `flutter analyze`: one warning and exit 1 for the unchanged,
  pre-existing unused import at
  `lib/chat/workspace_session.dart:6`; `git diff` confirms Task 5 did not modify
  that file.
- Core `flutter test`: success, 32 tests passed.
- Shell `flutter test`: success, 22 tests passed.
- `flutter build windows --debug`: success; produced
  `build/windows/x64/runner/Debug/digitalbrain_flutter.exe` using the real
  MiniAudio output backend.
- `flutter build web --release --wasm`: success; produced `build/web`. It emits
  the repository's existing missing CupertinoIcons font warning but no build
  error.
- `git diff --check`: success (Git emitted only line-ending conversion notices).
- Forbidden path/audio-log source scan: no matches.

## Self-review and concerns

- PCM byte lists are copied at protocol boundaries so later recorder/plugin
  buffer reuse cannot mutate queued or decoded frames.
- Output rejects any packet that is not exactly 3,840 bytes rather than
  padding, resampling, or pretending it played.
- Capture config changes away from 24 kHz mono are surfaced as unavailable;
  there is no implicit resampler or alternate encoder.
- Independent cleanup futures prevent one failing stop operation from skipping
  the other resources, while error details shown to users avoid raw payloads.
- No real microphone/speaker loopback or live CUDA PersonaPlex model session
  was available in this task environment. Windows and web compilation plus
  deterministic capture/playback/socket seams were verified; device-level
  audio continuity remains an end-to-end hardware acceptance item.
- A deployed web host must use the successful `--wasm` build path or configure
  COOP/COEP. Without that deployment configuration the deliberate behavior is
  `unavailable`, not silent output.
- The unrelated full-shell analyzer warning in `workspace_session.dart` remains
  intentionally untouched.
