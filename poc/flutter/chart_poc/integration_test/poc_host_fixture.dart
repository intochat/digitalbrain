import 'dart:async';
import 'dart:convert';
import 'dart:io';

final class PocHostFixture {
  PocHostFixture._(
    this.baseUri,
    this.ownerSessionToken,
    this._process,
    this._lines,
    this._standardError,
    this._standardErrorSubscription,
  );

  final Uri baseUri;
  final String ownerSessionToken;
  final Process _process;
  final StreamIterator<String> _lines;
  final StringBuffer _standardError;
  final StreamSubscription<String> _standardErrorSubscription;
  var _disposed = false;

  static Future<PocHostFixture> startApprovedElonCandidate({
    bool emitMalformedPostReadinessRecord = false,
  }) async {
    final chartRoot = Directory.current.absolute;
    final pocRoot = chartRoot.parent.parent;
    final executable = File(
      '${pocRoot.path}${Platform.pathSeparator}tests${Platform.pathSeparator}'
      'DigitalBrain.Poc.Flutter.Fixture${Platform.pathSeparator}'
      'bin${Platform.pathSeparator}'
      'Release${Platform.pathSeparator}net11.0${Platform.pathSeparator}'
      'DigitalBrain.Poc.Flutter.Fixture.exe',
    );
    if (!executable.existsSync()) {
      throw StateError(
        'The Release POC host must be built before Flutter integration.',
      );
    }

    final process = await Process.start(executable.path, [
      pocRoot.path,
      if (emitMalformedPostReadinessRecord) '--malformed-after-ready',
    ], workingDirectory: executable.parent.path);
    final lines = StreamIterator<String>(
      process.stdout.transform(utf8.decoder).transform(const LineSplitter()),
    );
    final standardError = StringBuffer();
    final standardErrorSubscription = process.stderr
        .transform(utf8.decoder)
        .listen(standardError.write);
    try {
      final ready = await _readJson(lines);
      if (ready['kind'] != 'ready') {
        throw StateError('POC host fixture did not become ready: $ready');
      }

      final baseUri = Uri.parse(ready['baseUri']! as String);
      final token = ready['ownerSessionToken']! as String;
      if (baseUri.host != InternetAddress.loopbackIPv4.address ||
          baseUri.scheme != 'http' ||
          token.isEmpty) {
        throw StateError(
          'POC host fixture returned an unsafe readiness handshake.',
        );
      }

      return PocHostFixture._(
        baseUri,
        token,
        process,
        lines,
        standardError,
        standardErrorSubscription,
      );
    } catch (error, stackTrace) {
      final cleanup = await _cleanupProcess(
        process,
        lines,
        standardErrorSubscription,
      );
      if (cleanup.error != null) {
        throw StateError(
          'POC host readiness failed: $error; cleanup failed: ${cleanup.error}; '
          'exit ${cleanup.exitCode}; stderr: $standardError',
        );
      }

      Error.throwWithStackTrace(error, stackTrace);
    }
  }

  Future<void> fireTrustedSocialPost({
    required String author,
    required String postId,
  }) async {
    final response = await _send({
      'id': _requestId(),
      'command': 'fire-social',
      'author': author,
      'postId': postId,
    });
    if (response['success'] != true) {
      throw StateError(
        'POC host rejected the trusted social fixture: $response',
      );
    }
  }

  Future<void> disposeAndVerifyDeleted() async {
    if (_disposed) {
      return;
    }

    _disposed = true;
    Map<String, Object?>? disposed;
    Object? protocolError;
    late final ({int? exitCode, Object? error}) cleanup;
    try {
      final response = await _send({'id': _requestId(), 'command': 'shutdown'});
      if (response['success'] != true) {
        throw StateError('POC host rejected fixture shutdown: $response');
      }

      disposed = await _nextJson();
    } catch (error) {
      protocolError = error;
    } finally {
      cleanup = await _cleanupProcess(
        _process,
        _lines,
        _standardErrorSubscription,
      );
    }
    if (protocolError != null) {
      throw StateError(
        'POC host fixture protocol failed: $protocolError; exit ${cleanup.exitCode}; '
        'cleanup: ${cleanup.error}; stderr: $_standardError',
      );
    }

    if (cleanup.error != null) {
      throw StateError(
        'POC host fixture teardown failed: ${cleanup.error}; '
        'exit ${cleanup.exitCode}; stderr: $_standardError',
      );
    }

    final artifacts = disposed?['artifacts'];
    if (disposed?['kind'] != 'disposed' ||
        artifacts is! List<Object?> ||
        artifacts.isNotEmpty ||
        cleanup.exitCode != 0) {
      throw StateError(
        'POC host fixture teardown failed: $disposed, exit ${cleanup.exitCode}, '
        'stderr: $_standardError',
      );
    }
  }

  Future<Map<String, Object?>> _send(Map<String, Object?> request) async {
    _process.stdin.writeln(jsonEncode(request));
    await _process.stdin.flush();
    final response = await _nextJson();
    if (response['id'] != request['id']) {
      throw StateError('POC host fixture response correlation failed.');
    }

    return response;
  }

  Future<Map<String, Object?>> _nextJson() => _readJson(_lines);

  static Future<({int? exitCode, Object? error})> _cleanupProcess(
    Process process,
    StreamIterator<String> lines,
    StreamSubscription<String> standardErrorSubscription,
  ) async {
    Object? cleanupError;
    int? exitCode;
    try {
      exitCode = await _terminateTree(process);
    } catch (error) {
      cleanupError = error;
    } finally {
      try {
        await lines.cancel();
      } catch (error) {
        cleanupError ??= error;
      }
      try {
        await standardErrorSubscription.cancel();
      } catch (error) {
        cleanupError ??= error;
      }
    }

    return (exitCode: exitCode, error: cleanupError);
  }

  static Future<int> _terminateTree(Process process) async {
    try {
      await process.stdin.close();
    } catch (_) {}

    try {
      return await process.exitCode.timeout(const Duration(seconds: 20));
    } on TimeoutException {
      if (Platform.isWindows) {
        final taskkill = await Process.run('taskkill', [
          '/pid',
          '${process.pid}',
          '/t',
          '/f',
        ]);
        if (taskkill.exitCode != 0 && !process.kill()) {
          throw StateError('Could not terminate the POC fixture process tree.');
        }
      } else if (!process.kill(ProcessSignal.sigkill)) {
        throw StateError('Could not terminate the POC fixture process.');
      }

      return process.exitCode.timeout(const Duration(seconds: 20));
    }
  }

  static Future<Map<String, Object?>> _readJson(
    StreamIterator<String> lines,
  ) async {
    final hasLine = await lines.moveNext().timeout(const Duration(minutes: 3));
    if (!hasLine) {
      throw StateError('POC host fixture closed its readiness protocol.');
    }

    final decoded = jsonDecode(lines.current);
    if (decoded is! Map) {
      throw StateError(
        'POC host fixture returned a malformed protocol record.',
      );
    }

    return decoded.cast<String, Object?>();
  }

  static String _requestId() =>
      DateTime.now().microsecondsSinceEpoch.toRadixString(16);
}
