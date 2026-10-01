// One product host serves the whole assembly (see IntoChatHostFixture); facts share its scripted
// model and database fixtures, so they run strictly one at a time.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
[assembly: Xunit.AssemblyFixture(typeof(IntoChat.Tests.E2E.IntoChatHostFixture))]
