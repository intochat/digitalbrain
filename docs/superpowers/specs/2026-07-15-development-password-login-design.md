# Development Password Login Design

## Goal

Replace the development bootstrap-secret prompt and automatic secret injection with a conventional username/password login for local and Development-stage Flutter clients. The shared credentials are `admin` / `admin`. Production continues to require external OIDC authentication.

## Scope

This change removes the development UI bootstrap-secret mechanism end to end. It does not remove DigitalBrain's internal session, access-token, refresh-token, signed-action, grant, owner/actor, or effect-authorization boundaries.

The feature applies when the backend uses `RuntimeProfile.Development`. `RuntimeProfile.Production` must reject password-login configuration and continue to require complete OIDC configuration.

## Alternatives Considered

### Exchange username/password for the existing session

This is the selected approach. Credentials are submitted once over the existing HTTPS gRPC transport. The backend validates them and creates the same bounded owner-scoped session used today. Existing refresh, feed, action, and logout behavior remains intact.

### Send HTTP Basic credentials on every request

Rejected because it would repeatedly expose the password to transport code, duplicate authentication across feed and action calls, and discard the existing session revocation and refresh model.

### Remove authentication and sessions entirely

Rejected because sessions carry the owner, actor, grants, audience, revocation state, and access required by feeds and signed actions. Removing them would weaken or require redesigning conversation isolation, OAuth ownership, and effect authorization.

## User Experience

The Development sign-in card displays separate `Username` and `Password` fields.

- Username is prefilled with `admin`.
- Password is prefilled with `admin` and visually obscured.
- Enter from either field and the Sign in button submit the form.
- An invalid login displays one generic error and does not identify which credential failed.
- After an invalid login, both fields are restored to the Development defaults.
- Signing out returns to the form with both fields restored to the Development defaults.
- The submitted password is never shown in error messages, logs, telemetry, surfaces, or screenshots.
- Production does not display or prefill the Development password form.

The prefilled values are deliberate Development convenience defaults. They are not compiled into a Production authentication path.

## Protocol and Client Flow

The existing session-creation RPC remains the transport boundary because it also supports OIDC bearer authentication. Its request changes as follows:

- Remove the bootstrap `secret` field and reserve its protobuf field number.
- Add bounded `username` and `password` string fields with new field numbers.
- Continue allowing an empty credential body when an OIDC bearer identity is supplied.

Flutter runtime APIs use login terminology rather than bootstrap-secret terminology. The transport submits username/password once and receives the existing `SessionReply`. `SessionController` continues to retain session credentials only in memory, refresh access automatically, reject identity changes during refresh, and clear protected state when authentication expires or the user signs out.

The Flutter environment no longer reads `DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET`. Manually launched and Aspire-launched Development clients therefore follow the same visible login flow.

## Backend Authentication

Development credential options use these defaults:

- Username: `admin`
- Password: `admin`
- Owner identity: existing configured owner or `local-owner`
- Actor identity: existing configured actor or `flutter-ui`
- Grants: the existing bounded Development UI grant set

Preserving the current owner and actor defaults keeps existing Development conversations, feeds, proposals, and grants accessible. The display login name does not redefine durable runtime identity.

The authenticator validates bounded non-empty inputs and compares a digest of the complete username/password pair in fixed time. It returns only success plus the configured `RuntimeRequestContext`, or failure. It does not expose partial-match information.

Production behavior is fail-closed:

- Password authentication is disabled.
- Supplying Development username or password configuration causes startup validation to fail.
- OIDC issuer, audience, subject mapping, grants, and token validation remain unchanged.

## Aspire Composition

Delete the `runtime-ui-bootstrap-secret` parameter, random-secret generation, backend secret environment variable, Flutter secret environment variable, and secret-bearing desktop-client method arguments.

The Development backend uses the profile-gated credential defaults unless explicitly overridden through server-side Development configuration. Credentials are never injected into the Flutter process. The Flutter executable receives only its transport endpoint and waits for the user to submit the prefilled form.

AppHost topology tests must prove that:

- No bootstrap-secret parameter exists.
- No bootstrap-secret environment variable reaches the backend or Flutter resource.
- The Flutter resource still references and waits for the HTTPS transport.
- Production publish topology does not enable password authentication.

## Data and Security Impact

Every person using `admin/admin` in a given Development deployment receives the same configured owner, actor, and grants. They therefore share conversations, feeds, proposals, connector authorizations, and permitted effects. This deployment must remain network-restricted and must not contain production credentials or production data.

HTTPS remains mandatory. Session access and refresh credentials remain internal implementation details and are never entered by the user. Signed surface actions, action expiry, audience checks, OAuth grant ownership, effect approval, encrypted state, and audit identity remain unchanged.

No migration is required because owner and actor defaults do not change. Existing session records may expire naturally; the first password login creates a new session for the same durable scope.

## Error Handling

- Empty or oversized username/password input fails before authentication.
- Incorrect credentials produce the existing safe unauthenticated transport outcome and generic Flutter copy.
- Authentication failure clears any prior protected in-memory surface and feed state.
- Session refresh failure returns the user to the prefilled Development login form.
- Production startup fails if Development password settings are supplied.
- Credentials and credential-derived values are excluded from structured logs, traces, exception messages, and surface payloads.

## Automated Testing

Backend tests cover:

- Development defaults accept exactly `admin/admin`.
- Incorrect username, incorrect password, empty input, and oversized input fail identically.
- Successful login issues the configured owner, actor, grants, UI audience, and normal session lifetime.
- Production disables password login and rejects Development credential configuration.
- OIDC authentication still creates a session without username/password fields.
- Refresh, logout, audience, revocation, signed-action, owner-scope, and grant tests remain green.
- AppHost topology contains no bootstrap-secret parameter or environment injection.

Flutter tests cover:

- Development form initially contains username `admin` and an obscured password value `admin`.
- Enter and the Sign in button submit both fields.
- A failed login renders generic copy and restores both defaults.
- A successful login renders the authenticated surface.
- Sign-out clears protected state and restores the prefilled form.
- Transport serialization uses username/password fields and never the deleted secret field.
- OIDC configuration continues to render the external-identity flow instead of Development credentials.

Required automated gates are:

- `dotnet test --logger "console;verbosity=minimal"`
- From `app`, `dart pub global run very_good_cli:very_good test`
- From `app`, `dart run tool/check_ui_imports.dart`
- From `app`, `dart format --output=none --set-exit-if-changed .`
- `dotnet format Brain.slnx --verify-no-changes --no-restore --verbosity minimal`
- `git diff --check origin/master...HEAD`

## Computer-Use Acceptance Test

Run the Development stack through Aspire and wait for the backend and Windows Flutter resources to become healthy. Use Windows computer control to exercise only visible application behavior:

1. Focus the Aspire-managed Flutter window.
2. Verify the username field visibly contains `admin` and the password field contains obscured characters.
3. Replace the password with an incorrect value and submit.
4. Verify the generic rejection copy and that the Development defaults are restored.
5. Submit the restored `admin/admin` form.
6. Verify the authenticated chat surface loads and can submit one harmless informational prompt.
7. Sign out and verify the prefilled login form returns.
8. Capture screenshots of the initial form, generic failure, authenticated chat, and signed-out form without revealing an unobscured password.
9. Inspect Aspire structured and console logs for the literal username, password, submitted wrong password, authorization headers, access tokens, and refresh tokens; none may appear.

## Completion Criteria

- No user-facing bootstrap secret, token, key, or sign-in code remains in Development Flutter UI.
- `admin/admin` is correctly prefilled and can authenticate local and Development-stage clients.
- Bootstrap-secret generation, configuration, transport, and tests are deleted.
- Internal sessions and downstream authorization invariants remain intact.
- Production password authentication is impossible by configuration or request.
- Automated and computer-use acceptance tests pass with no credential leakage.
