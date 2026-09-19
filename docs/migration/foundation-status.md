# Foundation migration — partial product
Base: 563c60c120ddfd78801db1e731219b3f7b55a8d2.
Approved scope: foundation and Time pilot only. Do not merge or deploy.
The full DigitalBrain.slnx is the product inventory, not the currently supported build.
Unmigrated: BehaviorRuntime, Aspire adapters, MCP, AI, Memory, Google, Microsoft,
Salesforce, ClickHouse, Supabase, Excel, Coding, Flutter, IntoChat.
No compatibility facade. Old command/reaction replay guarantees are intentionally retired.

The production timer-report.cs file app builds and is exercised by a separate-process TCP integration test. Shared module tests retain fast in-process transport.
