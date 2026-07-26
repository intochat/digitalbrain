## Windows execution boundary

### Use LPAC plus a Job Object, not `AssemblyLoadContext`

An AppContainer token carries a package SID and capability SIDs. Windows grants access as the
intersection of the normal user/group access and the AppContainer side of the DACL. AppContainers
run at low integrity and are isolated from other processes, windows, devices, files, registry,
network, and credentials unless access is granted. A Less Privileged AppContainer (LPAC) also
opts out of access granted through `ALL_APPLICATION_PACKAGES`, so it is the correct default for a
worker that should see nothing except explicitly brokered resources.
([Microsoft: launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer),
[token information classes](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class))

`AssemblyLoadContext` may still resolve an admitted Behavior assembly inside the worker, but it
provides dependency identity and unloading, not hostile-code isolation. The security boundary is
the operating-system process.

Construct the worker with a single `CreateProcessW` call using `STARTUPINFOEX`. Populate the
attribute list before process creation with:

1. `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`, containing the exact profile SID and no ambient
   capability SIDs;
2. `PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY` with
   `PROCESS_CREATION_ALL_APPLICATION_PACKAGES_OPT_OUT`, which creates the LPAC behavior;
3. `PROC_THREAD_ATTRIBUTE_JOB_LIST`, assigning the process to the prepared Job Object atomically;
4. `PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY` with
   `PROCESS_CREATION_CHILD_PROCESS_RESTRICTED`;
5. a compatibility-proven `PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY`.

Assigning the job through the process attribute closes the start-before-`AssignProcessToJobObject`
escape window. Windows documents the job-list and child-process attributes on
[`UpdateProcThreadAttribute`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute).

Configure the job with:

- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`;
- `JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 1`;
- bounded process and job memory;
- a CPU-rate hard cap appropriate for the host;
- no breakaway flags.

The broker owns a safe job handle for the entire execution and calls `TerminateJobObject` on
deadline, cancellation, protocol violation, or broker shutdown. Child-process restriction and
the active-process limit are intentional defense in depth.
([Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[`JOBOBJECT_BASIC_LIMIT_INFORMATION`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information))

### Do not blindly enable every mitigation

Mitigation policy is fixed before the process starts. A baseline prototype should test DEP,
SEHOP, heap termination, bottom-up/high-entropy ASLR, strict handle checks, extension-point
disablement, font disablement, remote-image blocking, System32 preference, and Win32k disablement.

Do not enable these without proving that the exact self-contained .NET worker still starts and
loads an admitted assembly:

- dynamic-code prohibition conflicts with normal managed JIT and dynamic assembly loading;
- Microsoft-signed-binary-only policies can reject the DigitalBrain worker and dependencies;
- strict CFG or CET combinations may be incompatible with a particular runtime and native
  dependency set.

The implementation plan must include a mitigation compatibility matrix. A policy bit is not
security if it silently forces the launcher to turn off the entire sandbox.

### Use CsWin32 rather than handwritten P/Invoke

`Microsoft.Windows.CsWin32` is a Microsoft source generator over Windows metadata. Its
`NativeMethods.txt` allowlist generates architecture-appropriate signatures, supporting types,
friendly overloads, and safe handles without a runtime dependency. Add it as
`PrivateAssets="all"` to one Windows-only launcher project.
([CsWin32 getting started](https://microsoft.github.io/CsWin32/docs/getting-started.html),
[features](https://microsoft.github.io/CsWin32/docs/features.html),
[NuGet 0.3.298](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.298))

The initial allowlist should contain only the APIs actually needed, including the profile,
process-attribute, process, job, token-inspection, and SID-release functions. Generated handles
must own their resources; do not spread `nint`, raw SID pointers, unions, or `unsafe` code through
the host.

This is still a prototype gate, not permission to assume generator coverage. Compile and execute
the real launcher on every supported Windows architecture and verify each generated constant,
union, and ownership rule.

### Publish the worker self-contained

LPAC intentionally cannot perform broad registry or filesystem discovery. A framework-dependent
worker may need to find the system `dotnet` host, installed runtime, or files not accessible to its
SID. Publish a self-contained `win-x64` worker for the first supported platform. NativeAOT is not
appropriate because the worker must load an approved Behavior assembly at runtime.

A provisional profile strategy is one AppContainer profile per installed
`(OwnerId, BehaviorId, RevisionDigest)`. Use a non-PII, fixed-length hash as the moniker (the
profile API limits it to 64 characters), place or ACL only the worker and exact immutable revision
to that SID, and remove the profile at uninstall.
[`CreateAppContainerProfile`](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createappcontainerprofile)
creates per-user profile directories and registry state.

Grant that SID read/execute access to the worker and exact installed revision, never write access.
Only the execution's bounded temporary/profile area is writable. Stage content into a new
directory, verify it, apply the final ACL, and atomically expose it; a Behavior must not be able to
replace bytes that a later execution will load under the same revision digest.

That strategy must be measured before it becomes architecture:

- compare profile-per-revision, profile-per-execution, and an ACL'd shared worker directory;
- measure creation, cleanup, installation, and cold-start cost;
- if a SID is reused, initially serialize executions for that revision and wipe its writable
  profile area so concurrent runs cannot read each other's temporary data.

### Prove the boundary at runtime

The worker should report its token evidence to the trusted broker. The broker verifies
`TokenIsAppContainer`, `TokenAppContainerSid`, `TokenIntegrityLevel`,
`TokenIsLessPrivilegedAppContainer`, and the capability list before sending any Behavior input.

Automated negative tests must prove that the launched worker cannot:

- open an arbitrary parent-profile file or registry key;
- access the network;
- spawn a child process;
- connect to another execution's pipe;
- exceed memory, CPU, deadline, or output limits;
- survive broker/job-handle termination.

Fail closed on a non-Windows host until another operating-system sandbox adapter has equivalent
tests. A mock launcher is suitable for domain tests, never for a production fallback.

## IPC and capability brokering

Windows requires AppContainer named pipes to live under the `LOCAL\` namespace. Use a high-entropy
pipe name such as `LOCAL\DigitalBrain\<random>` at the .NET level
(`\\.\pipe\LOCAL\...` in native notation). The default named-pipe DACL is too broad for this
boundary: Windows documents default access for system, administrators, creator owner, Everyone,
and anonymous identities.
([AppContainer IPC](https://learn.microsoft.com/en-us/windows/apps/develop/communication/interprocess-communication),
[named-pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights))

Use Kestrel's shipped named-pipe transport with HTTP/2. Configure
`NamedPipeTransportOptions.PipeSecurity` with an explicit DACL granting only the broker identity
and exact AppContainer SID. `CurrentUserOnly` is insufficient because it distinguishes user and
elevation level, not one Behavior worker. Set finite `MaxReadBufferSize` and
`MaxWriteBufferSize`; the API explicitly warns that zero or `null` disables backpressure and makes
unbounded buffering a security risk with untrusted clients.
([Kestrel named-pipe transport options](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.transport.namedpipes.namedpipetransportoptions?view=aspnetcore-10.0),
[Microsoft gRPC named-pipe IPC example](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0))

The client connects through `NamedPipeClientStream` using
`SocketsHttpHandler.ConnectCallback`, following Microsoft's shipped IPC pattern. The shared
framework already contains the transport; no separate transport package is needed.

The protocol still needs application authorization:

- one high-entropy execution ID and one-use bearer secret delivered through inherited launch data,
  never command-line logging;
- a handshake binding execution ID, revision digest, protocol version, and nonce;
- bounded request/response sizes and stable DTO versions;
- exactly one execution per channel;
- broker-side capability allowlist and budget checks on every call;
- no Orleans client, Azure credential, storage connection, signing key, or service provider inside
  the worker.

Pipe secrecy is not enough. A DACL identifies who may connect; the one-use protocol credential
binds the connection to the expected launch.
