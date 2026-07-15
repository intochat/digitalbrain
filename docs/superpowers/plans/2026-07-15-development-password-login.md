# Development Password Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Development/Test Flutter bootstrap-secret flow with a visible, prefilled `admin` / `admin` username/password login while preserving the existing internal access session, refresh session, owner/actor scope, grants, signed actions, and Production OIDC path.

**Architecture:** Keep `BootstrapSession` as the session-creation RPC because OIDC also uses it, but reserve the deleted secret field and add username/password fields. A profile-gated backend authenticator validates the complete credential pair once and issues the existing bounded UI session. Flutter sends the pair only during login, then continues using its in-memory access/refresh session. Aspire stops creating or injecting any UI credential.

**Tech Stack:** .NET 10, ASP.NET Core gRPC, protobuf, Aspire 13.4 AppHost, xUnit, Flutter/Dart, `grpc`/`protobuf`, Flutter widget tests, Windows computer use.

## Global Constraints

- Work directly in the existing `master` checkout. Do not create a branch, worktree, or second workspace.
- Preserve and never stage unrelated generated Flutter changes already present under `app/linux`, `app/macos`, `app/windows`, and `app/pubspec.lock`.
- Before API-touching edits, retry Context7 for ASP.NET Core gRPC, protobuf/Dart generation, Aspire, and Flutter text-field APIs. If the quota is still exhausted, record that fact and use the repository's pinned APIs and existing patterns without inventing version-specific behavior.
- Run CodeGraph impact queries and `aspire doctor` plus the live resource listing before the first source edit and after each integrated slice.
- Follow test-driven development: change the owning test first, run it and observe the expected failure, make the smallest production change, rerun to green, then commit only that slice.
- Do not add source comments. Generated Dart must remain comment-free under the repository rule.
- Never log, trace, interpolate, screenshot, or include username/password values in exceptions. Authentication failures must remain generic.
- `RuntimeProfile.Production` must disable password login and reject either Development credential configuration key. `RuntimeProfile.Development` and `RuntimeProfile.Test` use the local defaults.
- Use `DigitalBrain:Runtime:Ui:DevelopmentUsername` and `DigitalBrain:Runtime:Ui:DevelopmentPassword` as the only server-side override keys. Defaults are exactly `admin` and `admin`.
- Bound each submitted credential to 256 characters, reject empty strings, and compare a digest of the complete length-delimited pair with `CryptographicOperations.FixedTimeEquals`.
- Use `password` terminology only for the one-time Development login. Existing access tokens, refresh tokens, action tokens, session audience, revocation, and authorization behavior stay unchanged.

## Task 1: Establish a Safe Baseline on `master`

**Files:** None.

- [ ] Confirm the active checkout and capture the user-owned dirty files.

  Run: `git branch --show-current`
  Expected: `master`.

  Run: `git status --short --branch`
  Expected: `master` is ahead only by approved planning commits; the pre-existing Flutter generated files and `app/pubspec.lock` remain unstaged.

- [ ] Query CodeGraph for the callers and implementations of `UiBootstrapOptions`, `UiBootstrapAuthenticator`, `BootstrapSessionRequest`, `SessionTransport.bootstrapSession`, `RuntimeController.authenticateWithBootstrap`, and `AddDefaultDevFlutterClient`.

  Expected: the impact list is contained by the paths named in Tasks 2–5.

- [ ] Run `aspire doctor` and list current resources through the Aspire tooling.

  Expected: doctor is healthy; record whether `mcp` and `flutter-ui` are running so only affected resources are restarted later.

- [ ] Verify the current backend baseline.

  Run: `dotnet test tests/DigitalBrain.OrleansTests/DigitalBrain.OrleansTests.csproj --logger "console;verbosity=minimal"`
  Expected: all tests pass.

- [ ] Verify the current Flutter auth baseline without changing dependencies.

  Run from `app`: `flutter test test/runtime/grpc_ui_transport_test.dart test/runtime/session_state_test.dart test/runtime/runtime_controller_test.dart test/runtime/runtime_shell_test.dart test/runtime_test.dart`
  Expected: all selected tests pass.

## Task 2: Replace the Wire Secret and Backend Authenticator

**Files:**

- Modify: `src/DigitalBrain.Mcp/Protos/ui.proto`
- Regenerate: `app/lib/grpc/ui.pb.dart`
- Regenerate: `app/lib/grpc/ui.pbenum.dart`
- Regenerate: `app/lib/grpc/ui.pbgrpc.dart`
- Modify: `src/DigitalBrain.Mcp/UiGrpcService.cs`
- Modify: `src/DigitalBrain.Mcp/UiHostingExtensions.cs`
- Test: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiExternalIdentityTests.cs`
- Test: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiGrpcServiceTests.cs`

- [ ] Replace the bootstrap-secret option tests with Development credential tests before changing production code.

  Cover these exact cases in `UiExternalIdentityTests.cs`:

  ```csharp
  var defaults = UiDevelopmentLoginOptions.FromConfiguration(
      Configuration(new Dictionary<string, string?>()),
      RuntimeProfile.Development);
  var authenticator = new UiDevelopmentLoginAuthenticator(defaults);

  Assert.True(authenticator.TryAuthenticate("admin", "admin", out var context));
  Assert.Equal("local-owner", context.OwnerId.Value);
  Assert.Equal("flutter-ui", context.ActorId.Value);
  Assert.False(authenticator.TryAuthenticate("wrong", "admin", out _));
  Assert.False(authenticator.TryAuthenticate("admin", "wrong", out _));
  Assert.False(authenticator.TryAuthenticate(string.Empty, "admin", out _));
  Assert.False(authenticator.TryAuthenticate("admin", string.Empty, out _));
  Assert.False(authenticator.TryAuthenticate(new string('a', 257), "admin", out _));
  Assert.False(authenticator.TryAuthenticate("admin", new string('a', 257), out _));
  ```

  Add Production assertions for each key independently and together. Also assert the disabled Production authenticator rejects `admin/admin`.

- [ ] Change `UiGrpcServiceTests.cs` to submit `Username = "admin"` and `Password = "admin"`, rename secret fixtures to login fixtures, and add one test proving all invalid pair variants return the same `StatusCode.Unauthenticated` with the same safe status detail.

  Extend the successful local-login assertion to validate the issued session through `RuntimeSessionAuthority`: owner `owner`, actor `principal`, grants `brain.read` and `ui.action`, audience `digitalbrain-v2-ui`, and an access expiry approximately 15 minutes after issuance.

  Add `V2_oidc_login_accepts_an_empty_credential_body`. Give the test call's `DefaultHttpContext` an `IAuthenticationService` stub that returns a validated OIDC principal, send an empty `BootstrapSessionRequest` plus a bearer header, and assert the created session has `AuthAssurance.Oidc`. This must exercise `UiGrpcService`, not only `UiExternalIdentityOptions`.

  Run: `dotnet test tests/DigitalBrain.OrleansTests/DigitalBrain.OrleansTests.csproj --logger "console;verbosity=minimal"`
  Expected: compilation or assertions fail because the new protobuf fields and authenticator types do not exist yet.

- [ ] Change the protobuf request while preserving wire compatibility.

  ```proto
  message BootstrapSessionRequest {
    reserved 1;
    string username = 2;
    string password = 3;
  }
  ```

- [ ] Regenerate the tracked Dart gRPC files from the repository root.

  Run: `protoc --proto_path=src/DigitalBrain.Mcp/Protos --dart_out=grpc:app/lib/grpc src/DigitalBrain.Mcp/Protos/ui.proto`
  Expected: only `ui.pb.dart`, `ui.pbenum.dart`, and `ui.pbgrpc.dart` are generated; `BootstrapSessionRequest` exposes `username` and `password`, and no `secret` accessor remains.

  Run: `dotnet build src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj --no-restore`
  Expected: C# protobuf generation succeeds, while old service/test references identify the remaining migration points.

- [ ] Replace `UiBootstrapOptions` with `UiDevelopmentLoginOptions` and `UiBootstrapAuthenticator` with `UiDevelopmentLoginAuthenticator`.

  Use this public shape:

  ```csharp
  public sealed record UiDevelopmentLoginOptions(
      string Username,
      string Password,
      BrainOwnerId OwnerId,
      ActorId ActorId,
      TimeSpan AccessLifetime,
      IReadOnlySet<string> Grants,
      bool Enabled = true);

  public sealed class UiDevelopmentLoginAuthenticator(UiDevelopmentLoginOptions options)
  {
      public bool TryAuthenticate(
          string suppliedUsername,
          string suppliedPassword,
          out RuntimeRequestContext context);
  }
  ```

  `FromConfiguration` must:

  - detect presence separately for `DevelopmentUsername` and `DevelopmentPassword`;
  - reject either key in Production even if its value is empty;
  - return a disabled Production option;
  - default non-Production values to `admin/admin`;
  - retain `local-owner`, `flutter-ui`, the 15-minute access lifetime, and the existing grant set;
  - reject empty or over-256 configured credentials and invalid owner/actor values.

- [ ] Implement one fixed-time comparison over the complete pair, using an unambiguous length prefix before each UTF-8 value.

  Use this algorithmic shape without logging either input:

  ```csharp
  private static byte[] DigestPair(string username, string password)
  {
      var usernameBytes = Encoding.UTF8.GetBytes(username);
      var passwordBytes = Encoding.UTF8.GetBytes(password);
      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      hash.AppendData(BitConverter.GetBytes(usernameBytes.Length));
      hash.AppendData(usernameBytes);
      hash.AppendData(BitConverter.GetBytes(passwordBytes.Length));
      hash.AppendData(passwordBytes);
      return hash.GetHashAndReset();
  }
  ```

  Validate both supplied strings before hashing. The failure path returns only `false` and a default context.

- [ ] Update `UiGrpcService.BootstrapSession` to keep OIDC first and fall back to Development login only when no external identity was presented.

  The decisive branch must be equivalent to:

  ```csharp
  else if (external.Status == UiExternalAuthenticationStatus.Rejected ||
           !developmentLogin.TryAuthenticate(
               request.Username,
               request.Password,
               out bootstrapContext))
  {
      logger.LogWarning("UI session login was denied.");
      throw Unauthenticated();
  }
  ```

  Rename the successful local authentication-kind trace value from `bootstrap` to `password`. Do not attach inputs or digests to logs or activities.

- [ ] Update `UiHostingExtensions.AddUiTransport` to register the renamed options and authenticator. Keep JWT bearer registration and external identity configuration unchanged.

- [ ] Make the backend tests green.

  Run: `dotnet test tests/DigitalBrain.OrleansTests/DigitalBrain.OrleansTests.csproj --logger "console;verbosity=minimal"`
  Expected: all Orleans tests pass, including existing refresh, logout, audience, revocation, signed-action, owner-scope, and grant coverage.

- [ ] Inspect the diff for accidental credential exposure and commit only this slice.

  Run: `git diff --check`
  Run: `rg -n -i "request\.(Username|Password)|supplied(Username|Password)|DevelopmentPassword" src/DigitalBrain.Mcp tests/DigitalBrain.OrleansTests`
  Expected: matches occur only in validation/authentication code and tests, never in logging or interpolation.

  Run: `git add src/DigitalBrain.Mcp/Protos/ui.proto src/DigitalBrain.Mcp/UiGrpcService.cs src/DigitalBrain.Mcp/UiHostingExtensions.cs app/lib/grpc/ui.pb.dart app/lib/grpc/ui.pbenum.dart app/lib/grpc/ui.pbgrpc.dart tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiExternalIdentityTests.cs tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiGrpcServiceTests.cs`
  Commit: `feat(auth): exchange development credentials for ui sessions`

## Task 3: Rename the Flutter Session API and Serialize Both Fields

**Files:**

- Modify: `app/lib/runtime/grpc_ui_transport.dart`
- Modify: `app/lib/runtime/session_state.dart`
- Modify: `app/lib/runtime/runtime.dart`
- Modify: `app/lib/runtime/runtime_session_owner.dart`
- Modify: `app/lib/runtime/runtime_configuration.dart`
- Test: `app/test/runtime/grpc_ui_transport_test.dart`
- Test: `app/test/runtime/session_state_test.dart`
- Test: `app/test/runtime/runtime_controller_test.dart`
- Test: `app/test/runtime_test.dart`
- Test fixtures in: `app/test/runtime/runtime_shell_test.dart`

- [ ] Change `grpc_ui_transport_test.dart` first so the local login test calls `login(username: 'admin', password: 'admin')` and asserts both protobuf fields. Change the OIDC test to assert both fields are empty.

  ```dart
  final session = await transport.login(
    username: 'admin',
    password: 'admin',
  );

  expect(port.bootstrapRequest?.username, 'admin');
  expect(port.bootstrapRequest?.password, 'admin');
  ```

  Run from `app`: `flutter test test/runtime/grpc_ui_transport_test.dart`
  Expected: compilation fails because `login` does not exist and the generated fields are not wired into the transport.

- [ ] Rename the session boundary consistently.

  ```dart
  abstract interface class SessionTransport {
    Future<SessionBundle> login({
      required String username,
      required String password,
    });

    Future<SessionBundle> refreshSession({required String refreshToken});
    Future<void> logout({required String refreshToken});
  }
  ```

  Rename `SessionController.bootstrap` to `login`, validate both non-empty inputs, and delegate to `transport.login`. Keep `_bootstrap` only if it is renamed to a neutral internal name such as `_establish`; no bootstrap-secret terminology may remain.

- [ ] Implement `GrpcUiTransport.login` with a `BootstrapSessionRequest(username: username, password: password)` and the same audience-only metadata and unary timeout. Keep `bootstrapExternalSession` sending an empty request plus the bearer header.

- [ ] Rename the runtime entry point to `RuntimeController.authenticateWithPassword({required String username, required String password})` and remove the `bootstrapSecret` parameter from `start()`.

  `start()` must enter `RuntimeStatus.awaitingSignIn` whenever no in-memory session exists. `_authenticate`, session refresh, feed binding, scope clearing, and logout behavior remain unchanged.

- [ ] Remove `bootstrapSecret` from `RuntimeConfiguration`, delete the `DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET` environment read, and simplify `RuntimeSessionOwner._start` so it only restores OIDC or calls `controller.start()`.

  Rename the owner entry point to:

  ```dart
  void authenticateWithPassword({
    required String username,
    required String password,
  })
  ```

- [ ] Mechanically migrate all fake transports and auth calls in the named Dart tests from one secret to the username/password pair. Preserve race, cancellation, refresh, identity-change, and protected-state assertions; only the login input shape should change.

  Run from `app`: `rg -n "bootstrapSecret|authenticateWithBootstrap|bootstrapSession\(String" lib test`
  Expected: no matches.

- [ ] Run the focused runtime suite.

  Run from `app`: `flutter test test/runtime/grpc_ui_transport_test.dart test/runtime/session_state_test.dart test/runtime/runtime_controller_test.dart test/runtime_test.dart`
  Expected: all selected tests pass.

- [ ] Format and commit the Flutter session/transport slice.

  Run from `app`: `dart format lib/runtime/grpc_ui_transport.dart lib/runtime/session_state.dart lib/runtime/runtime.dart lib/runtime/runtime_session_owner.dart lib/runtime/runtime_configuration.dart test/runtime/grpc_ui_transport_test.dart test/runtime/session_state_test.dart test/runtime/runtime_controller_test.dart test/runtime_test.dart test/runtime/runtime_shell_test.dart`
  Run: `git diff --check`
  Run: `git add app/lib/runtime/grpc_ui_transport.dart app/lib/runtime/session_state.dart app/lib/runtime/runtime.dart app/lib/runtime/runtime_session_owner.dart app/lib/runtime/runtime_configuration.dart app/test/runtime/grpc_ui_transport_test.dart app/test/runtime/session_state_test.dart app/test/runtime/runtime_controller_test.dart app/test/runtime_test.dart app/test/runtime/runtime_shell_test.dart`
  Commit: `refactor(auth): use password login across flutter runtime`

## Task 4: Build the Prefilled Development Login Form

**Files:**

- Modify: `app/lib/runtime/widgets/runtime_shell.dart`
- Test: `app/test/runtime/runtime_shell_test.dart`

- [ ] Replace the manual sign-in-code widget test first with tests for the complete visible contract:

  - Development form has separate `runtimeUsernameFieldKey` and `runtimePasswordFieldKey` fields.
  - Both controllers initially contain `admin`.
  - Only the password field has `obscureText == true`.
  - Enter from either field and the button submit both values.
  - A rejected login displays exactly `Sign-in was not accepted. Please try again.` and restores both fields to `admin`.
  - A successful login displays the authenticated surface.
  - Sign-out clears protected state and returns both defaults.
  - An external-identity configuration displays `Continue` and neither Development field.

  Run from `app`: `flutter test test/runtime/runtime_shell_test.dart`
  Expected: compilation/assertion failures for the missing field keys and old sign-in-code UI.

- [ ] Replace the secret controller and key with explicit Development login state.

  Use these constants and controllers:

  ```dart
  const String developmentUsername = 'admin';
  const String developmentPassword = 'admin';
  const Key runtimeUsernameFieldKey = Key('v2-runtime-username-field');
  const Key runtimePasswordFieldKey = Key('v2-runtime-password-field');

  final TextEditingController _username = TextEditingController(
    text: developmentUsername,
  );
  final TextEditingController _password = TextEditingController(
    text: developmentPassword,
  );
  ```

  Dispose both controllers and remove `runtimeSecretFieldKey`.

- [ ] Render `Username` and `Password` text fields only when `_session.hasExternalIdentity` is false. Disable suggestions and autocorrect for the password field, obscure it, and submit from either `onSubmitted` callback.

- [ ] Replace sign-in-code copy with conventional Development copy and one generic error. Do not identify the wrong field and do not display either submitted value.

- [ ] Implement `_resetDevelopmentCredentials()` and call it immediately after copying both submitted strings into local variables. Call it again in the sign-out button callback before `_session.signOut()`.

  Submit using:

  ```dart
  final username = _username.text;
  final password = _password.text;
  _resetDevelopmentCredentials();
  _session.authenticateWithPassword(
    username: username,
    password: password,
  );
  ```

- [ ] Make the widget suite green and verify that no cleartext password appears as a `Text` widget.

  Run from `app`: `flutter test test/runtime/runtime_shell_test.dart`
  Expected: all widget tests pass.

- [ ] Run all focused Flutter auth/runtime tests together to catch fixture drift.

  Run from `app`: `flutter test test/runtime/grpc_ui_transport_test.dart test/runtime/session_state_test.dart test/runtime/runtime_controller_test.dart test/runtime/runtime_shell_test.dart test/runtime_test.dart`
  Expected: all selected tests pass.

- [ ] Format and commit the UI slice.

  Run from `app`: `dart format lib/runtime/widgets/runtime_shell.dart test/runtime/runtime_shell_test.dart`
  Run: `git diff --check`
  Run: `git add app/lib/runtime/widgets/runtime_shell.dart app/test/runtime/runtime_shell_test.dart`
  Commit: `feat(auth): add prefilled development login form`

## Task 5: Delete Aspire Bootstrap-Secret Composition

**Files:**

- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.AppHost/Composition/FlutterAspireExtensions.cs`
- Test: `tests/DigitalBrain.AppHostTests/AppHostTopologyTests.cs`

- [ ] Change `TestProfile_DeclaresFlutterClientWiredOnlyToTransport` first so it asserts:

  - no resource named `runtime-ui-bootstrap-secret` exists;
  - the desktop Flutter environment contains only the transport endpoint from this feature;
  - neither backend nor Flutter environment has a key containing `BootstrapSecret`, `DevelopmentUsername`, or `DevelopmentPassword`;
  - desktop Flutter references only `mcp` and still waits until it is healthy;
  - web Flutter retains OIDC dart-defines and no password configuration;
  - Production has no local Flutter resource and no local password environment/configuration.

  Run: `dotnet test tests/DigitalBrain.AppHostTests/DigitalBrain.AppHostTests.csproj --logger "console;verbosity=minimal"`
  Expected: assertions fail because the secret parameter and environment references still exist.

- [ ] Delete `uiBootstrapSecret`, its random generation, its backend environment injection, and every relationship assertion built around it from `AppHost.cs`.

  Keep the local desktop resource creation behind `enableDevFlutter`:

  ```csharp
  if (enableDevFlutter)
  {
      _ = ctx.AddDefaultDevFlutterClient(mcp, endpointName: "https")
          ?? throw new InvalidOperationException(
              "Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");
  }
  ```

  Keep the existing conditional browser/OIDC resource inside the same local UI block.

- [ ] Delete `BootstrapSecretEnvironmentVariable` and the `ParameterResource bootstrapSecret` arguments from both desktop Flutter extension methods. The desktop resource must receive only `DIGITALBRAIN_V2_UI_ENDPOINT`, reference the HTTPS endpoint, and wait for `mcp`.

- [ ] Simplify the desktop target validation message so it no longer says `secret-bearing`.

- [ ] Make the topology tests green.

  Run: `dotnet test tests/DigitalBrain.AppHostTests/DigitalBrain.AppHostTests.csproj --logger "console;verbosity=minimal"`
  Expected: all AppHost tests pass.

- [ ] Build affected projects and inspect the live topology.

  Run: `dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --no-restore`
  Run: `dotnet build src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj --no-restore`
  Expected: both builds succeed.

  Run `aspire doctor`, restart only `mcp` and `flutter-ui` through Aspire resource commands, and list resources again.
  Expected: both affected resources return to a healthy/running state and no credential parameter resource exists.

- [ ] Commit the composition deletion.

  Run: `git diff --check`
  Run: `git add hosts/DigitalBrain.AppHost/AppHost.cs hosts/DigitalBrain.AppHost/Composition/FlutterAspireExtensions.cs tests/DigitalBrain.AppHostTests/AppHostTopologyTests.cs`
  Commit: `refactor(auth): remove bootstrap secret composition`

## Task 6: Prove There Is No Legacy Auth Path or Credential Leak

**Files:** All changed source and tests.

- [ ] Scan code and tests for removed names and user-facing language.

  Run from the repository root:

  ```powershell
  rg -n -i "DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET|runtime-ui-bootstrap-secret|BootstrapSecret|bootstrapSecret|authenticateWithBootstrap|runtimeSecretFieldKey|sign-in code" src hosts app/lib app/test tests
  ```

  Expected: no matches.

- [ ] Scan production code for accidental logging or interpolation of credential fields.

  Run:

  ```powershell
  rg -n -i "Log.*(username|password)|SetTag.*(username|password)|WriteLine.*(username|password)|\$.*(username|password)" src hosts app/lib
  ```

  Expected: no matches involving the Development credentials.

- [ ] Run the exact root .NET gate in the background and poll it while running Flutter gates.

  Run: `dotnet test --logger "console;verbosity=minimal"`
  Expected: all projects pass; the known baseline total is 535 tests unless deliberate test additions increase it.

- [ ] Run the complete Flutter test gate.

  Run from `app`: `dart pub global run very_good_cli:very_good test`
  Expected: all tests pass; the known baseline total is 179 tests unless deliberate test additions increase it.

- [ ] Run Flutter architecture and formatting gates.

  Run from `app`: `dart run tool/check_ui_imports.dart`
  Expected: exits 0.

  Run from `app`: `dart format --output=none --set-exit-if-changed .`
  Expected: exits 0 with no changed files.

- [ ] Run .NET formatting and diff gates.

  Run: `dotnet format Brain.slnx --verify-no-changes --no-restore --verbosity minimal`
  Expected: exits 0.

  Run: `dotnet format deploy/DigitalBrain.Deploy.csproj --verify-no-changes --no-restore --verbosity minimal`
  Expected: exits 0.

  Run: `git diff --check origin/master...HEAD`
  Expected: no whitespace errors.

- [ ] Run `aspire doctor` and inspect resource health, recent structured logs, console logs, and traces for `mcp` and `flutter-ui`.

  Expected: doctor passes; no new errors, crash loops, or rejected startup configuration.

- [ ] Commit any test-only corrections required by the integrated gates without mixing in user-owned generated files.

  Commit if needed: `test(auth): cover development password login boundaries`

## Task 7: Windows Computer-Use Acceptance Through Aspire

**Files:** No source changes expected. Store temporary screenshots outside tracked repository paths.

- [ ] Ensure the Development stack is running from the `master` checkout.

  Run if the AppHost is not already active: `aspire run --project hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
  Expected: `mcp` and `flutter-ui` start successfully; the Flutter process receives the endpoint but no username/password environment values.

- [ ] Invoke the `computer-use:computer-use` skill and focus the Aspire-managed Windows Flutter window.

- [ ] Capture the initial form. Verify the visible username is `admin`; inspect the password field as obscured characters without revealing it; verify there is no token, key, secret, or sign-in-code copy.

- [ ] Replace the password with the unique wrong value `wrong-password-acceptance` and submit.

  Expected: only `Sign-in was not accepted. Please try again.` appears, username returns to `admin`, and the password field returns to an obscured default.

- [ ] Submit the restored `admin/admin` form.

  Expected: the authenticated chat surface loads and the sign-out control appears.

- [ ] Submit one harmless informational prompt: `Summarize what this local DigitalBrain can do without changing any data.`

  Expected: the prompt is accepted and an authenticated response/surface is rendered without requesting another credential.

- [ ] Sign out.

  Expected: protected chat state is no longer visible, and the prefilled Development login form returns with username `admin` and an obscured password.

- [ ] Save screenshots of the initial form, generic failure, authenticated chat, and signed-out form. Inspect each image before retaining it; none may reveal an unobscured password, authorization header, access token, or refresh token.

- [ ] Query Aspire structured and console logs for all of the following literals and patterns:

  - `wrong-password-acceptance`
  - `admin/admin`
  - `DevelopmentPassword`
  - `authorization: Bearer`
  - known access-token and refresh-token field names or captured values

  Expected: zero credential/token disclosures. A generic denied-login event and a generic successful session event are allowed.

- [ ] Run `aspire doctor` one final time.

  Expected: all checks pass after login, chat, and logout.

## Task 8: Final Review and Planning-Artifact Cleanup

**Files:**

- Delete after every prior checkbox is complete: `docs/superpowers/specs/2026-07-15-development-password-login-design.md`
- Delete after every prior checkbox is complete: `docs/superpowers/plans/2026-07-15-development-password-login.md`

- [ ] Review `git diff origin/master...HEAD --stat` and `git diff origin/master...HEAD` against every design requirement. Confirm the net change deletes bootstrap-secret plumbing and does not introduce a second session/auth system.

- [ ] Confirm the only modified/staged files are intentional feature files; preserve all unrelated Flutter generated working-tree changes.

  Run: `git status --short`
  Expected: feature changes are committed; user-owned generated files remain unstaged and untouched.

- [ ] After all automated and computer-use evidence is recorded, remove the completed design and execution plan to comply with the repository's living-document rule.

  Run: `git rm docs/superpowers/specs/2026-07-15-development-password-login-design.md docs/superpowers/plans/2026-07-15-development-password-login.md`
  Commit: `docs: remove completed auth planning artifacts`

- [ ] Run the completion verification one last time after cleanup.

  Run: `dotnet test --logger "console;verbosity=minimal"`
  Run from `app`: `dart pub global run very_good_cli:very_good test`
  Run: `git diff --check origin/master...HEAD`
  Expected: every command passes, and no required work remains.
