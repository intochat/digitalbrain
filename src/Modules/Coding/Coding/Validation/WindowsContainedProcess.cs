using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DigitalBrain.Coding;

// JOB_LIST associates the process atomically at creation, closing the suspended-process orphan window.
// https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812
public sealed class WindowsContainedProcess : IDisposable
{
    private readonly SafeFileHandle? _job;
    public Process Process { get; }
    public StreamReader Output { get; }
    public StreamReader Error { get; }

    public static WindowsContainedProcess StartWithoutJob(string fileName, IReadOnlyList<string> arguments, string directory,
        IReadOnlyDictionary<string, string>? additionalEnvironment = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        if (additionalEnvironment is not null)
        {
            foreach (var item in additionalEnvironment) { start.Environment[item.Key] = item.Value; }
        }
        var process = new Process { StartInfo = start };
        if (!process.Start()) { throw new InvalidOperationException("The behavior process did not start."); }
        return new WindowsContainedProcess(null, process, process.StandardOutput, process.StandardError);
    }

    private WindowsContainedProcess(SafeFileHandle? job, Process process, StreamReader output, StreamReader error)
    {
        _job = job;
        Process = process;
        Output = output;
        Error = error;
    }

    private WindowsContainedProcess(SafeFileHandle job, Process process, SafeFileHandle output, SafeFileHandle error)
    {
        _job = job;
        Process = process;
        Output = new(new FileStream(output, FileAccess.Read));
        Error = new(new FileStream(error, FileAccess.Read));
    }

    public static WindowsContainedProcess Start(string fileName, IReadOnlyList<string> arguments, string directory,
        IReadOnlyDictionary<string, string>? additionalEnvironment = null)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("Managed behavior execution currently requires Windows job containment."); }
        if (!Path.IsPathFullyQualified(fileName)) { throw new ArgumentException("Executable path must be absolute.", nameof(fileName)); }
        var job = Native.CreateJobObjectW(0, null);
        if (job.IsInvalid) { throw new Win32Exception(); }
        SafeFileHandle? outputRead = null;
        SafeFileHandle? errorRead = null;
        nint attributes = 0, jobValue = 0, handlesValue = 0, environment = 0;
        var initialized = false;
        try
        {
            var limits = new Native.ExtendedLimits { Basic = new() { Flags = 0x2000 } };
            if (!Native.SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<Native.ExtendedLimits>())) { throw new Win32Exception(); }
            var security = new Native.Security { Length = Marshal.SizeOf<Native.Security>(), Inherit = 1 };
            if (!Native.CreatePipe(out outputRead, out var outputWrite, ref security, 0)) { throw new Win32Exception(); }
            using var closeOutput = outputWrite;
            if (!Native.CreatePipe(out errorRead, out var errorWrite, ref security, 0)) { throw new Win32Exception(); }
            using var closeError = errorWrite;
            if (!Native.SetHandleInformation(outputRead, 1, 0) || !Native.SetHandleInformation(errorRead, 1, 0)) { throw new Win32Exception(); }
            // A real stdin handle is required with STARTF_USESTDHANDLES; EOF rather than inherited input.
            if (!Native.CreatePipe(out var inputRead, out var inputWrite, ref security, 0)) { throw new Win32Exception(); }
            using var closeInput = inputRead;
            inputWrite.Dispose();
            nuint size = 0;
            _ = Native.InitializeProcThreadAttributeList(0, 2, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!Native.InitializeProcThreadAttributeList(attributes, 2, 0, ref size)) { throw new Win32Exception(); }
            initialized = true;
            jobValue = Marshal.AllocHGlobal(nint.Size);
            Marshal.WriteIntPtr(jobValue, job.DangerousGetHandle());
            if (!Native.UpdateProcThreadAttribute(attributes, 0, 0x2000D, jobValue, (nuint)nint.Size, 0, 0)) { throw new Win32Exception(); }
            handlesValue = Marshal.AllocHGlobal(nint.Size * 3);
            Marshal.WriteIntPtr(handlesValue, 0, outputWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handlesValue, nint.Size, errorWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handlesValue, nint.Size * 2, inputRead.DangerousGetHandle());
            if (!Native.UpdateProcThreadAttribute(attributes, 0, 0x20002, handlesValue, (nuint)(nint.Size * 3), 0, 0)) { throw new Win32Exception(); }
            var startup = new Native.StartupEx
            {
                Startup = new()
                {
                    Size = Marshal.SizeOf<Native.StartupEx>(),
                    Flags = 0x100,
                    StandardInput = inputRead.DangerousGetHandle(),
                    StandardOutput = outputWrite.DangerousGetHandle(),
                    StandardError = errorWrite.DangerousGetHandle()
                },
                Attributes = attributes,
            };
            var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "DOTNET_ROOT", "NUGET_PACKAGES" })
            { if (Environment.GetEnvironmentVariable(key) is { } value) { values[key] = value; } }
            values["MSBUILDNODEREUSE"] = "0";
            values["MSBUILDTERMINALLOGGER"] = "off";
            values["DOTNET_CLI_UI_LANGUAGE"] = "en";
            values["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            foreach (var pair in additionalEnvironment ?? new Dictionary<string, string>())
            {
                if (pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0')) { throw new ArgumentException("Invalid environment value."); }
                values[pair.Key] = pair.Value;
            }
            // A contained process inherits the caller's W3C trace context so its work stays on the
            // same trace. An explicit value from the caller wins over the ambient activity.
            if (AmbientTraceParent() is { } traceParent && !values.ContainsKey("TRACEPARENT")) { values["TRACEPARENT"] = traceParent; }
            if (Activity.Current?.TraceStateString is { Length: > 0 } traceState && !values.ContainsKey("TRACESTATE")) { values["TRACESTATE"] = traceState; }
            environment = Marshal.StringToHGlobalUni(string.Join('\0', values.Select(x => x.Key + "=" + x.Value)) + "\0\0");
            var command = new StringBuilder(string.Join(' ', new[] { fileName }.Concat(arguments).Select(Quote)));
            if (!Native.CreateProcessW(fileName, command, 0, 0, true, 0x08080404, environment, directory, ref startup, out var info))
            { throw new Win32Exception(); }
            using var processHandle = new SafeFileHandle(info.Process, true);
            using var threadHandle = new SafeFileHandle(info.Thread, true);
            var process = Process.GetProcessById(info.ProcessId);
            if (Native.ResumeThread(threadHandle) == uint.MaxValue) { process.Dispose(); throw new Win32Exception(); }
            return new(job, process, outputRead, errorRead);
        }
        catch
        {
            job.Dispose();
            outputRead?.Dispose();
            errorRead?.Dispose();
            throw;
        }
        finally
        {
            if (initialized) { Native.DeleteProcThreadAttributeList(attributes); }
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(jobValue);
            Marshal.FreeHGlobal(handlesValue);
            Marshal.FreeHGlobal(environment);
        }
    }

    public void Terminate()
    {
        if (_job is null)
        {
            try { if (!Process.HasExited) { Process.Kill(entireProcessTree: true); } } catch (Exception) { }
            return;
        }
        _job.Dispose();
    }
    public void Dispose() { _job?.Dispose(); Output.Dispose(); Error.Dispose(); Process.Dispose(); }

    private static string? AmbientTraceParent()
        => Activity.Current is { } activity
            ? "00-" + activity.TraceId.ToHexString() + "-" + activity.SpanId.ToHexString()
                + "-" + (activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00")
            : null;

    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Security { public int Length; public nint Descriptor; public int Inherit; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public nuint MinWorking, MaxWorking; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential)] internal struct Startup { public int Size; public nint Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public ushort Show, ReservedSize; public nint ReservedBytes, StandardInput, StandardOutput, StandardError; }
        [StructLayout(LayoutKind.Sequential)] internal struct StartupEx { public Startup Startup; public nint Attributes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public nint Process, Thread; public int ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits info, int length);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref Security security, uint size);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
        [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returnedSize);
        [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(nint list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, ref StartupEx startup, out ProcessInfo info);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(SafeFileHandle thread);
    }
}