// Compatibility shim: Reqnroll 3.3.4 code generator targets xunit.v3 2.x API shapes.
// This project runs xunit.v3 3.x which changed IAsyncLifetime from Task to ValueTask
// and removed Xunit.Abstractions. SkippableFactAttribute requires Xunit.SkippableFact.
//
// This file bridges those gaps. Remove once Reqnroll ships xunit.v3 3.x support.

namespace Xunit.Abstractions
{
    // Was promoted to Xunit.ITestOutputHelper in xunit.v3 3.x.
    public interface ITestOutputHelper : global::Xunit.ITestOutputHelper { }
}

namespace Xunit
{
    // Reqnroll generates [SkippableFactAttribute] on each scenario. Map to [Fact].
    // xUnit3003 requires a public constructor with string callerFilePath and int callerLineNumber
    // for source information tracking in xunit.v3 3.x.
#pragma warning disable xUnit3003
    [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
    public sealed class SkippableFactAttribute : global::Xunit.FactAttribute
    {
        public SkippableFactAttribute() { }
        public SkippableFactAttribute(
            [global::System.Runtime.CompilerServices.CallerFilePath] string? sourceFile = null,
            [global::System.Runtime.CompilerServices.CallerLineNumber] int sourceLine = 0)
            : base(sourceFile ?? "", sourceLine) { }
    }
#pragma warning restore xUnit3003
}
