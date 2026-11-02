using System.Threading.Tasks;
using DigitalBrain.Analyzers.Contracts;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Contracts;

public sealed class ShareableContractAnalyzerFacts
{
    [Fact]
    public Task Valid_shareable_interface_reports_nothing() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using DigitalBrain.Access;
        [Shareable]
        public interface IValidService { }
        """);

    [Fact]
    public Task Shareable_attribute_on_class_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using DigitalBrain.Access;
        [Shareable]
        public class {|DB0031:NotAnInterface|} { }
        """);

    [Fact]
    public Task Cross_brain_invocation_without_shareable_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;
        public interface IUnshareable { Task DoWork(); }
        public sealed class Caller : App
        {
            public Task Invoke() => {|DB0031:Get<IUnshareable>("brain:other-brain")|}.DoWork();
        }
        """);

    [Fact]
    public Task Local_invocation_without_shareable_reports_nothing() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;
        public interface ILocalService { Task DoWork(); }
        public sealed class Caller : App
        {
            public Task Invoke() => Get<ILocalService>("local-service").DoWork();
        }
        """);

    [Fact]
    public Task Method_with_stream_parameter_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IBadContract
        {
            Task {|DB0031:ProcessStream|}(Stream stream);
        }
        """);

    [Fact]
    public Task Method_with_stream_return_type_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IBadContract
        {
            Task<Stream> {|DB0031:GetStream|}();
        }
        """);

    [Fact]
    public Task Method_with_direct_stream_return_type_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using DigitalBrain.Access;
        [Shareable]
        public interface IBadContract
        {
            Stream {|DB0031:GetStream|}();
        }
        """);

    [Fact]
    public Task Method_with_unannotated_class_parameter_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public class UnannotatedPayload { }
        [Shareable]
        public interface IBadContract
        {
            Task {|DB0031:SendPayload|}(UnannotatedPayload payload);
        }
        """);

    [Fact]
    public Task Method_with_valid_shareable_types_reports_nothing() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        using Orleans;

        [GenerateSerializer]
        public sealed class CustomData
        {
            public int Id { get; set; }
        }

        [Alias("my.aliased.data")]
        public sealed class AliasedData
        {
            public string Text { get; set; } = string.Empty;
        }

        public record SimpleRecord(string Name, int Value);

        public enum Status { Active, Inactive }

        [Shareable]
        public interface IGoodContract
        {
            Task DoWork();
            Task<int> Calculate(int a, double b, decimal c, bool flag, Guid id, DateTime dt, DateTimeOffset dto, TimeSpan ts, Status status, CancellationToken ct);
            ValueTask<string> GetName(byte[] rawData);
            Task<CustomData> GetData(SimpleRecord record, AliasedData aliased);
            Task<byte[]> DownloadBytes(string key);
            void Reset();
        }
        """);

    [Fact]
    public Task Challenger_review_scenario_reports_DB0031_for_stream_param_and_unshareable_payload() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Access;

        public class NonShareablePayload { }

        [Shareable]
        public interface IBadShareableContract
        {
            Task {|DB0031:ProcessStream|}(Stream stream, CancellationToken ct);
            NonShareablePayload {|DB0031:GetPayload|}();
        }
        """);

    [Fact]
    public Task Ref_parameter_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IRefContract
        {
            Task {|DB0031:MethodWithRef|}(ref int counter);
        }
        """);

    [Fact]
    public Task Out_parameter_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IOutContract
        {
            Task {|DB0031:MethodWithOut|}(out string result);
        }
        """);

    [Fact]
    public Task Out_parameter_with_valid_record_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public record MyData(int X);
        [Shareable]
        public interface IOutContract
        {
            Task {|DB0031:MethodWithOutRecord|}(out MyData data);
        }
        """);

    [Fact]
    public Task Unannotated_class_return_type_in_task_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public class UnannotatedResult { }
        [Shareable]
        public interface IUnannotatedReturnContract
        {
            Task<UnannotatedResult> {|DB0031:GetResult|}();
        }
        """);

    [Fact]
    public Task Direct_primitive_return_without_task_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using DigitalBrain.Access;
        [Shareable]
        public interface IDirectReturnContract
        {
            int {|DB0031:Calculate|}();
            string {|DB0031:GetName|}();
            bool {|DB0031:IsActive|}();
        }
        """);

    [Fact]
    public Task Void_and_task_and_valuetask_returns_pass() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        using Orleans;

        public enum Mode { Fast, Slow }
        public record ConfigRecord(string Host, int Port);

        [GenerateSerializer]
        public sealed class SerializedPayload
        {
            public int Code { get; set; }
        }

        [Alias("app.alias")]
        public sealed class AliasedPayload
        {
            public string Name { get; set; } = "";
        }

        [Shareable]
        public interface ISubContract
        {
            Task Ping();
        }

        [Shareable]
        public interface IValidComprehensiveContract
        {
            void Reset();
            Task Execute();
            Task<int> GetCount();
            Task<string> GetString();
            Task<byte[]> GetBytes();
            Task<Mode> GetMode();
            Task<ConfigRecord> GetConfig();
            Task<SerializedPayload> GetPayload();
            Task<AliasedPayload> GetAliased();
            Task<ISubContract> GetSub();
            Task<int?> GetNullableInt();
            ValueTask PingValue();
            ValueTask<int> GetCountValue();
            ValueTask<string> GetStringValue();
            ValueTask<byte[]> GetBytesValue();
            ValueTask<ConfigRecord> GetConfigValue();
            Task<List<string>> GetList();
            Task<Dictionary<string, int>> GetMap();
            Task<ImmutableArray<byte>> GetImmutable();
            Task<(int, string)> GetTuple();
            Task<Tuple<string, int>> GetClassTuple();
        }
        """);

    [Fact]
    public Task Nested_unshareable_collection_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Collections.Generic;
        using System.IO;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface INestedBadContract
        {
            Task<List<Stream>> {|DB0031:GetStreams|}();
            Task<Dictionary<string, Stream>> {|DB0031:GetStreamMap|}();
            Task {|DB0031:SendStreamMap|}(Dictionary<Stream, string> map);
        }
        """);

    [Fact]
    public Task System_object_parameter_and_return_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IObjectContract
        {
            Task {|DB0031:AcceptObject|}(object obj);
            Task<object> {|DB0031:ReturnObject|}();
            ValueTask<object> {|DB0031:ReturnValueObject|}();
        }
        """);

    [Fact]
    public Task Delegate_and_action_parameters_report_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface IDelegateContract
        {
            Task {|DB0031:RegisterCallback|}(Action callback);
            Task {|DB0031:RegisterFunc|}(Func<int, string> func);
        }
        """);

    [Fact]
    public Task Array_of_unshareable_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public class UnannotatedData { }
        [Shareable]
        public interface IArrayContract
        {
            Task {|DB0031:SendStreams|}(Stream[] streams);
            Task<UnannotatedData[]> {|DB0031:GetData|}();
        }
        """);

    [Fact]
    public Task Tuple_with_unshareable_element_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.IO;
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        [Shareable]
        public interface ITupleContract
        {
            Task {|DB0031:ProcessTuple|}((int Id, Stream Stream) input);
            Task<(string Name, Stream Stream)> {|DB0031:GetTuple|}();
        }
        """);

    [Fact]
    public Task Nullable_unshareable_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public struct UnannotatedStruct
        {
            public int Value;
        }
        [Shareable]
        public interface INullableContract
        {
            Task {|DB0031:SendStruct|}(UnannotatedStruct? s);
            Task<UnannotatedStruct?> {|DB0031:GetStruct|}();
        }
        """);

    [Fact]
    public Task Unannotated_interface_parameter_and_return_reports_DB0031() => Verify<ShareableContractAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain.Access;
        public interface IUnannotatedService { }
        [Shareable]
        public interface IServiceContract
        {
            Task {|DB0031:PassService|}(IUnannotatedService service);
            Task<IUnannotatedService> {|DB0031:GetService|}();
        }
        """);
}

