# Task 5 Report: Flutter native PersonaPlex voice

## Status and commit

Task 5 is implemented by commits
`f1465d2b10d679618f5978345373379f6878dec2`
(`feat: add native PersonaPlex voice workspace`) and
`5d47d2a6251f4e06c67cde874b4a64216cfde4c5`
(`fix: harden PersonaPlex Flutter lifecycle`). The evidence report is kept in
follow-up documentation-only commits so that it can cite immutable
implementation commits exactly.

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

The original implementation note about COOP/COEP was stale. The current 4.1.7
changelog states that 4.1.6 removed `-pthread`/`SharedArrayBuffer`, so
cross-origin isolation is no longer required. Current setup documentation does
require these two loader scripts, now present in `shell/web/index.html`:

- `assets/packages/flutter_soloud/web/libflutter_soloud_plugin.js`
- `assets/packages/flutter_soloud/web/init_module.dart.js`

Both standard JavaScript and Wasm web builds compile. If audio initialization
or playback nevertheless fails in an unsupported browser/device, the UI now
reports the accurate runtime diagnosis:

> Continuous 24 kHz mono PCM16 playback failed to initialize in this browser.
> Verify flutter_soloud web loader scripts and browser support.

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
- Core `flutter test`: success, 33 tests passed.
- Shell `flutter test`: success, 28 tests passed.
- `flutter build windows --debug`: success; produced
  `build/windows/x64/runner/Debug/digitalbrain_flutter.exe` using the real
  MiniAudio output backend.
- `flutter build web --release` and the earlier
  `flutter build web --release --wasm`: success. The standard build contains
  both required loader script references and both package assets. It emits the
  repository's existing missing CupertinoIcons font warning but no build error.
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
- Web no longer requires COOP/COEP with `flutter_soloud` 4.1.7. The required
  loader scripts are included; an actual browser/device initialization failure
  remains `unavailable`, not silent output.
- The unrelated full-shell analyzer warning in `workspace_session.dart` remains
  intentionally untouched.

## Review-fix addendum

Review hardening was completed test-first in
`5d47d2a6251f4e06c67cde874b4a64216cfde4c5`.

### RED/GREEN evidence

1. Voice deactivation during a delayed retry release was RED with two created
   sessions where one was expected. After adding a lifecycle generation and
   checking `mounted`, `active`, and generation after every `_start` await, the
   widget test creates no late controller and reports exactly one cleanup of
   capture, output, and socket.
2. Two synchronous Start invocations were RED with two controllers. GREEN now
   creates one session; unmount proves exactly one capture stop/dispose, output
   stop/dispose, and socket close. A controller produced by a stale generation
   is disposed before it can be retained.
3. WebSocket subscription cancellation was RED with
   `Bad state: cancel failed`; channel close was skipped. GREEN independently
   guards subscription cancellation, stop control, channel close, and event
   stream close, and verifies channel close count is one.
4. Recorder cleanup injected failures at config callback removal,
   `isRecording`, and recorder `stop`. RED observed source cancellation count
   zero. GREEN preserves the first failure while still cancelling the source,
   closing output, and directly disposing the recorder exactly once. Calling
   dispose twice reuses the cached result without a second native disposal.
5. Overlapping stale/release cleanup was RED with Flutter's
   `PersonaPlexVoiceController was used after being disposed` assertion. GREEN
   makes notifier disposal idempotent while retaining once-only resource
   cleanup.

### Review verification

- Focused core protocol/client: 6 passed.
- Focused controller, screen lifecycle, and workspace: 18 passed.
- Full core suite: 33 passed.
- Full shell suite: 28 passed.
- Core analyzer: no issues.
- Voice implementation/test analyzer: no issues.
- Full shell analyzer: only the unchanged
  `lib/chat/workspace_session.dart:6` unused-import warning.
- Windows debug build: passed.
- Standard JavaScript web release build: passed; Wasm dry run also passed.
- Built web output contains both required `flutter_soloud` loader scripts and
  assets.
- Forbidden native-path/raw-audio-log scan: no matches.

### Review-fix files

- `src/Modules/UI/Flutter/core/lib/src/personaplex_voice_client.dart`
- `src/Modules/UI/Flutter/core/test/personaplex_voice_protocol_test.dart`
- `src/Modules/UI/Flutter/shell/lib/voice/pcm_audio_output.dart`
- `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_controller.dart`
- `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_screen.dart`
- `src/Modules/UI/Flutter/shell/test/voice/personaplex_voice_controller_test.dart`
- `src/Modules/UI/Flutter/shell/test/voice/personaplex_voice_screen_test.dart`
- `src/Modules/UI/Flutter/shell/web/index.html`
