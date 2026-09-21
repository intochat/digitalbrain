// Browser hosts compile the same Flutter workspace; keep product deployments sequential.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]