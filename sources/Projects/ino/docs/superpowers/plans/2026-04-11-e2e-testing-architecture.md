# E2E Testing Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build Tier 3 E2E testing infrastructure. Aspire TestingHost boots the full ino topology with mocked externals. NeuronE2ETest base class makes every neuron testable from day 0. Travel domain has 6+ E2E tests proving gRPC to neurons to RFW pipeline works.

**Architecture:** Three-tier test architecture. iaw/Testing gets new AddIAWTesting/AddIAWSiloTesting/AddIAWClientTesting extensions mirroring production AddIAW with in-memory/mock equivalents. A slim test/E2E.AppHost uses these extensions. NeuronE2ETest base class wraps Aspire TestingHost as IClassFixture. Production code gets minimal Testing environment branches calling the testing extensions.

**Tech Stack:** Aspire.Hosting.Testing 13.1.2, xunit.v3, Orleans TestingHost, Microsoft.Extensions.AI (FunctionCallContent for tool-calling mock), gRPC

**Spec:** docs/superpowers/specs/2026-04-11-e2e-testing-architecture-design.md

---

### Task 1: Move shared mocks from Ino.Travel.Tests to iaw/Testing/Mocks

**Files:**
- Create: iaw/Testing/Mocks/MockSerpApiProvider.cs
- Create: iaw/Testing/Mocks/MockAirportValidator.cs
- Create: iaw/Testing/Mocks/NoOpTranscriptionService.cs
- Modify: iaw/Testing/Testing.csproj (add TripRadar application contracts reference)
- Modify: domains/travel/Ino.Travel.Tests/TravelTestFixture.cs (remove inline mocks, import from IAW.Testing)

The mock classes currently live inline inside TravelTestFixture.cs (lines 34-223). Move them to standalone files in iaw/Testing/Mocks/ so both Tier 2 BDD tests and Tier 3 E2E tests can share them.

- [ ] **Step 1:** Create iaw/Testing/Mocks/MockSerpApiProvider.cs. Copy the MockSerpApiProvider class from domains/travel/Ino.Travel.Tests/TravelTestFixture.cs:35-196. Change namespace to IAW.Testing.Mocks. Change visibility from internal sealed to public sealed.

- [ ] **Step 2:** Create iaw/Testing/Mocks/MockAirportValidator.cs. Copy the MockAirportValidator class from TravelTestFixture.cs:199-223. Change namespace to IAW.Testing.Mocks. Change visibility from internal sealed to public sealed.

- [ ] **Step 3:** Create iaw/Testing/Mocks/NoOpTranscriptionService.cs with a stub implementation of IAudioTranscriptionService that returns a placeholder string. Namespace: IAW.Testing.Mocks.

- [ ] **Step 4:** Update iaw/Testing/Testing.csproj. Add project references for the TripRadar application contracts needed by MockSerpApiProvider:
  - TripRadar.Server.Application
  - TripRadar.Server.Comms.Core
  - TripRadar.Server.Domain

- [ ] **Step 5:** Update domains/travel/Ino.Travel.Tests/TravelTestFixture.cs. Remove the inline MockSerpApiProvider and MockAirportValidator classes. Add using IAW.Testing.Mocks import. The TravelTestFixture class itself stays unchanged, just references the moved mocks.

- [ ] **Step 6:** Run dotnet build ino.slnx. Verify no compilation errors.

- [ ] **Step 7:** Run dotnet test domains/travel/Ino.Travel.Tests/ --verbosity normal. Verify all 12 existing travel BDD tests still pass.

- [ ] **Step 8:** Commit: refactor: move shared mocks to iaw/Testing/Mocks for cross-tier reuse

---

### Task 2: ToolCallingMockChat (FunctionCallContent-aware IChatClient)

**Files:**
- Create: iaw/Testing/ToolCallingMockChat.cs

This is the key enabler for Tier 3. It must return FunctionCallContent in ChatResponse messages so the Agent framework tool-calling middleware executes real grain methods.

Read iaw/Testing/NeuronBddHooks.cs:113-170 for the existing PromptMatchingMockChatClient pattern. Read iaw/Core/Agents/Agent.cs:252-275 (ProduceLlmStreamAsync) and Agent.Tools.cs:58-84 (GetAllTools) to understand how the Agent framework processes tool-call responses.

The MEAI tool-calling protocol: when IChatClient.GetResponseAsync returns a ChatResponse whose message contains FunctionCallContent items, the AIAgent middleware finds the matching AITool, invokes it, creates a FunctionResultContent message, and calls GetResponseAsync again with the result appended. The mock must handle this two-phase flow: first call returns tool-call, second call (with tool results) returns final text.

- [ ] **Step 1:** Create iaw/Testing/ToolCallingMockChat.cs implementing IChatClient with:
  - OnToolCall(string toolName, params (string param, object value)[] args) method that registers a tool-call scenario
  - OnMultiToolCall method for parallel tool calls
  - WithFinalResponse(string text) for the post-tool-call text response
  - CalledTools list and ToolCallCount for test assertions
  - Reset() to clear state between tests
  - GetResponseAsync: phase 1 returns FunctionCallContent for registered scenarios; phase 2 (when FunctionResultContent present in history) returns final text
  - GetStreamingResponseAsync that delegates to GetResponseAsync and yields the contents

- [ ] **Step 2:** Run dotnet build iaw/Testing/Testing.csproj. Verify compilation.

- [ ] **Step 3:** Commit: feat(testing): ToolCallingMockChat with FunctionCallContent for LLM tool-calling simulation

---

### Task 3: IAW Testing Extensions (hosting + silo + client)

**Files:**
- Create: iaw/Testing/IAWTestingHostingExtensions.cs
- Create: iaw/Testing/IAWTestingSiloExtensions.cs
- Create: iaw/Testing/IAWTestingClientExtensions.cs
- Modify: iaw/Testing/Testing.csproj (add Aspire.Hosting.Orleans, Aspire.Hosting.Testing)
- Modify: iaw/Aspire.Hosting/Aspire.Hosting.csproj (add InternalsVisibleTo for IAW.Testing)

These mirror the production AddIAW/AddIAWClient extensions but with in-memory Orleans, mock LLM, and no containers.

- [ ] **Step 1:** Add InternalsVisibleTo to iaw/Aspire.Hosting/Aspire.Hosting.csproj so iaw/Testing can access IAWService.Orleans (which is internal):
  Add ItemGroup with InternalsVisibleTo Include="IAW.Testing"

- [ ] **Step 2:** Update iaw/Testing/Testing.csproj. Add package references:
  - Aspire.Hosting.Orleans
  - Aspire.Hosting.Testing
  Add project references:
  - iaw/Aspire.Hosting/Aspire.Hosting.csproj
  - iaw/Aspire.Client/Aspire.Client.csproj

- [ ] **Step 3:** Create iaw/Testing/IAWTestingHostingExtensions.cs with:
  - AddIAWTesting(this IDistributedApplicationBuilder builder, string name = "iaw") returning IAWService. Creates Orleans with development clustering, memory grain storage for Default and PubSubStore, memory streaming, memory reminders. Returns new IAWService(orleans, builder) with NO Azure Storage, Qdrant, or parameters.
  - WithTestReference extending IResourceBuilder to call builder.WithReference(iaw.Orleans) only
  - WithTestClientReference extending IResourceBuilder to call builder.WithReference(iaw.Orleans.AsClient()) only

  Use the exact code from the spec lines 140-177.

- [ ] **Step 4:** Create iaw/Testing/IAWTestingSiloExtensions.cs with AddIAWSiloTesting. Configures Orleans with loopback endpoint, 5 min response timeout, VolatileStateMachineStorageProvider, BroadcastChannel, UseInMemoryDurableJobs. Registers ToolCallingMockChat as IChatClient (both as IChatClient and typed singleton), MockEmbeddingGenerator, IawMemoryProvider, HttpClient, GitHubClient stub. Use the exact code from the spec lines 186-225.

- [ ] **Step 5:** Create iaw/Testing/IAWTestingClientExtensions.cs with AddIAWClientTesting. Configures Orleans client with localhost clustering using "test" cluster/service IDs, 5 min response timeout, GatewayConnectionRetryFilter. No blob or qdrant references. Use the exact code from the spec lines 231-252.

- [ ] **Step 6:** Run dotnet build iaw/Testing/Testing.csproj. Verify compilation.

- [ ] **Step 7:** Commit: feat(testing): AddIAWTesting/Silo/Client extensions mirroring production without containers

---

### Task 4: Wire RFW templates into InoService.RouteTravelAsync

**Files:**
- Modify: iaw/Telegram/Services/InoService.cs:43-61

Read domains/travel/Ino.Travel/UI/FlightCardTemplate.cs, HotelCardTemplate.cs, PlaceCardTemplate.cs, DestinationCardTemplate.cs. Each has BuildList(JsonElement) returning (byte[] Description, byte[] Data).

The current RouteTravelAsync (lines 43-61) returns text and marks content_type = "travel_results" but never populates rfw_description/rfw_data. The ChatResponse proto already has these fields (fields 3-4 in iaw/Telegram/Protos/ino.proto:32-36).

- [ ] **Step 1:** Add using Ino.Travel.UI to InoService.cs imports.

- [ ] **Step 2:** Add a static TryBuildRfw method to InoService. It parses the reply as JSON, checks for a "type" property and "data" property, dispatches to the correct template builder based on type:
  - "flight_results" calls FlightCardTemplate.BuildList(root)
  - "hotel_results" calls HotelCardTemplate.BuildList(root)
  - "place_results" calls PlaceCardTemplate.BuildList(root)
  - "destination_results" calls DestinationCardTemplate.BuildList(root)
  Returns false for non-JSON replies, error responses, or unknown types.

- [ ] **Step 3:** Update RouteTravelAsync. After GetResponse, call TryBuildRfw on the reply. If it returns true, set response.RfwDescription = ByteString.CopyFrom(rfwDesc), response.RfwData = ByteString.CopyFrom(rfwData), response.ContentType = "travel_results".

- [ ] **Step 4:** Run dotnet build iaw/Telegram/Telegram.csproj. Verify compilation.

- [ ] **Step 5:** Commit: feat(telegram): wire RFW templates into RouteTravelAsync closing the gRPC-to-RFW pipeline gap

---

### Task 5: Testing environment branches in Agents.Host and Telegram

**Files:**
- Modify: iaw/Agents.Host/Program.cs
- Modify: iaw/Telegram/Program.cs
- Modify: iaw/Agents.Host/Agents.Host.csproj (add Testing project reference)
- Modify: iaw/Telegram/Telegram.csproj (add Testing project reference)

These are the minimal production code changes. Each project gets one environment check that calls the testing extensions from iaw/Testing.

- [ ] **Step 1:** Add ProjectReference to iaw/Testing/Testing.csproj in iaw/Agents.Host/Agents.Host.csproj.

- [ ] **Step 2:** Modify iaw/Agents.Host/Program.cs. Read the current file (lines 1-28). Restructure into Testing vs production branches as shown in the spec (lines 294-313):
  - Testing branch: call builder.AddIAWSiloTesting(), register MockSerpApiProvider as ISerpApiProviderService, register MockAirportValidator as IAirportValidationService
  - Production branch: call builder.AddIAW(), call builder.Services.AddTravelDomain(builder.Configuration, builder.Environment)
  - Both paths share: builder.UseOrleans with AddStartupTask, AddTimelineCapture, AddInoNew

- [ ] **Step 3:** Add ProjectReference to iaw/Testing/Testing.csproj in iaw/Telegram/Telegram.csproj.

- [ ] **Step 4:** Modify iaw/Telegram/Program.cs. Read the current file (lines 1-286). Add a Testing environment branch:
  - Testing: call builder.AddIAWClientTesting(), builder.Services.AddGrpc(), register NoOpTranscriptionService as IAudioTranscriptionService
  - Production: existing code unchanged (AddIAWClient, AddGrpc, blob storage, Telegram bot, webhooks, audio services, hosted services)
  - Both paths share: app.UseGrpcWeb(), app.UseStaticFiles(), app.MapGrpcService of InoService with EnableGrpcWeb(), the /ino POST endpoint, the /webhook POST endpoint, OTLP bridge endpoints, the root redirect

- [ ] **Step 5:** Run dotnet build ino.slnx. Verify full solution compiles.

- [ ] **Step 6:** Run dotnet test ino.slnx --verbosity normal. Verify all 468+ existing tests still pass (the Testing branch does not activate during normal test runs because existing tests use TestCluster, not Aspire TestingHost).

- [ ] **Step 7:** Commit: feat: testing environment branches in Agents.Host and Telegram calling iaw/Testing extensions

---

### Task 6: E2E.AppHost project

**Files:**
- Create: test/E2E.AppHost/E2E.AppHost.csproj
- Create: test/E2E.AppHost/AppHost.cs
- Create: test/E2E.AppHost/Properties/launchSettings.json
- Modify: ino.slnx (add the project)

The slim test AppHost that uses AddIAWTesting from iaw/Testing.

- [ ] **Step 1:** Create test/E2E.AppHost/E2E.AppHost.csproj using Aspire.AppHost.Sdk/13.2.2. Target net11.0. Reference:
  - iaw/Aspire.Hosting/Aspire.Hosting.csproj (IsAspireProjectResource=false)
  - iaw/Testing/Testing.csproj (IsAspireProjectResource=false)
  - iaw/Agents.Host/Agents.Host.csproj (project resource)
  - iaw/Telegram/Telegram.csproj (project resource)

- [ ] **Step 2:** Create test/E2E.AppHost/Properties/launchSettings.json with ASPNETCORE_ENVIRONMENT=Testing.

- [ ] **Step 3:** Create test/E2E.AppHost/AppHost.cs. Use builder.AddIAWTesting("iaw") from IAW.Testing namespace. Add Projects.Agents_Host as "assistant" with WithTestReference(iaw) and Orleans endpoint configuration. Add Projects.Telegram as "telegram" with WithTestClientReference(iaw) and WaitFor(assistant). Use the exact code from the spec lines 273-287.

- [ ] **Step 4:** Add to ino.slnx inside the /test/ folder: Project Path="test/E2E.AppHost/E2E.AppHost.csproj"

- [ ] **Step 5:** Run dotnet build test/E2E.AppHost/E2E.AppHost.csproj. Verify compilation.

- [ ] **Step 6:** Commit: feat: E2E.AppHost slim Aspire test topology using AddIAWTesting

---

### Task 7: InoTestHost fixture + NeuronE2ETest base class + FlightSearchE2E

**Files:**
- Create: iaw/Testing/InoTestHost.cs
- Create: test/E2E.Tests/Infrastructure/NeuronE2ETest.cs
- Create: test/E2E.Tests/Travel/FlightSearchE2E.cs
- Modify: test/E2E.Tests/E2E.Tests.csproj (add references)

InoTestHost lives in iaw/Testing (no gRPC dependency). NeuronE2ETest lives in test/E2E.Tests (needs gRPC proto types from Telegram).

- [ ] **Step 1:** Create iaw/Testing/InoTestHost.cs. It implements IAsyncLifetime (xunit.v3). InitializeAsync creates the Aspire TestingHost via DistributedApplicationTestingBuilder.CreateAsync of Projects.E2E_AppHost with --environment=Testing argument. Optionally opens browser when INO_E2E_OPEN_BROWSER=true. Exposes GetTelegramEndpoint() returning Uri. DisposeAsync stops and disposes the app.

- [ ] **Step 2:** Update test/E2E.Tests/E2E.Tests.csproj. Add package references for Aspire.Hosting.Testing, Grpc.Net.Client, Grpc.Net.Client.Web, Google.Protobuf, Grpc.Tools. Add project references for iaw/Testing, iaw/Telegram, domains/travel/Ino.Travel, test/E2E.AppHost. Add Protobuf Include pointing to iaw/Telegram/Protos/ino.proto with GrpcServices=Client.

- [ ] **Step 3:** Create test/E2E.Tests/Infrastructure/NeuronE2ETest.cs. Abstract base class with IClassFixture of InoTestHost. Primary constructor takes InoTestHost. Exposes:
  - Host property (InoTestHost)
  - Grpc property (lazy-created gRPC client using GrpcWebHandler connecting to Host.GetTelegramEndpoint)
  - ChatAsync(string message, string userId) calling Grpc.ChatAsync
  - GetRfwDescription(ChatResponse) decoding bytes to UTF-8 string
  - GetRfwData(ChatResponse) parsing bytes as JSON
  - AssertRfw(ChatResponse, widgetName, data tuples) asserting description contains widget name and data fields match
  - AssertRfwList(ChatResponse, widgetName, minItems) asserting list response

- [ ] **Step 4:** Create test/E2E.Tests/Travel/FlightSearchE2E.cs. Two Facts:
  - FindFlights_RendersFlightCards: Reset MockLlm, OnToolCall SearchFlights with JFK/DPS/2026-07-15, WithFinalResponse, ChatAsync, AssertRfw for FlightCard with airline/from/to
  - ExploreDestinations_RendersDestinationCards: OnToolCall ExploreDestinations with JFK, ChatAsync, AssertRfwList for FlightCard minItems 2

- [ ] **Step 5:** Run dotnet build test/E2E.Tests/E2E.Tests.csproj. Verify compilation.

- [ ] **Step 6:** Run dotnet test test/E2E.Tests/ --filter FlightSearchE2E --verbosity normal. This is the critical moment. If the tests pass, the full pipeline works: Aspire TestingHost -> gRPC -> InoService -> TravelRecommender -> FlightSearchNeuron -> MockSerpApi -> RFW FlightCard.

- [ ] **Step 7:** Commit: feat(e2e): InoTestHost + NeuronE2ETest + FlightSearchE2E proving full pipeline

---

### Task 8: Remaining travel E2E tests

**Files:**
- Create: test/E2E.Tests/Travel/HotelSearchE2E.cs
- Create: test/E2E.Tests/Travel/PlaceDiscoveryE2E.cs
- Create: test/E2E.Tests/Travel/TripPlanningE2E.cs
- Create: test/E2E.Tests/Travel/PriceTrackerE2E.cs
- Create: test/E2E.Tests/Travel/NeuronDiscoveryE2E.cs

All follow the same pattern: inherit NeuronE2ETest, configure MockLlm, call ChatAsync, assert with AssertRfw.

- [ ] **Step 1:** Create HotelSearchE2E.cs. OnToolCall SearchHotels with location Bali, checkIn 2026-07-15, checkOut 2026-07-25. AssertRfw for HotelCard with name "Grand Hyatt Bali", price 180, rating 4.5.

- [ ] **Step 2:** Create PlaceDiscoveryE2E.cs. OnToolCall FindPlaces with location Bali, type restaurant. AssertRfw for PlaceCard with name "Locavore", rating 4.8.

- [ ] **Step 3:** Create TripPlanningE2E.cs. OnMultiToolCall with SearchFlights + SearchHotels + FindPlaces for Tokyo. WithFinalResponse. Assert ToolCallCount == 3 and all three tool names in CalledTools.

- [ ] **Step 4:** Create PriceTrackerE2E.cs. Does NOT go through gRPC. Gets IPriceTracker grain directly. Tests TrackFlight returns tracking_started JSON, GetTrackedPrices returns non-empty array, StopTracking returns tracking_stopped. Note: need to access the cluster client. Add a helper to InoTestHost or use App.Services to get IClusterClient.

- [ ] **Step 5:** Create NeuronDiscoveryE2E.cs. Gets IAgentRegistry grain directly. Asserts ListByDomainAsync("travel") returns >= 7 neurons with correct display names. Asserts HybridSearchAsync("flights") returns FlightSearch.

- [ ] **Step 6:** Run dotnet test test/E2E.Tests/ --filter "Category=E2E" --verbosity normal. All tests should pass.

- [ ] **Step 7:** Commit: feat(e2e): hotel, place, trip-planning, price-tracker, and discovery E2E tests

---

### Task 9: Full solution integration and verification

**Files:**
- No new files. Verification only.

- [ ] **Step 1:** Run dotnet build ino.slnx. Expected: 0 errors.

- [ ] **Step 2:** Run dotnet test ino.slnx --verbosity normal. Expected: 480+ tests pass (468 existing + 8+ new E2E).

- [ ] **Step 3:** Verify existing Tier 2 travel BDD tests: dotnet test domains/travel/Ino.Travel.Tests/ --verbosity normal. Expected: all 12 BDD tests pass using mocks imported from iaw/Testing/Mocks.

- [ ] **Step 4:** Verify existing Tier 1 framework tests: dotnet test test/Core.Tests/ --verbosity normal. Expected: all pass, unaffected by changes.

- [ ] **Step 5:** Commit: feat(e2e): complete Tier 3 E2E testing architecture with all tests green
