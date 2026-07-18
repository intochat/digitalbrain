# Travel AI Assistant for Telegram — Requirements + Claude Code Prompt (v2)

## 1) Product concept
A Telegram bot with **Topics (threads) in a private dialog**:

1) **Home (main chat, threadId=0)** — **NO AI**  
   Deterministic UI for flight search + price tracking.

2) **Travel AI Topic** — LLM-based travel assistant.

3) **General AI Topic** — general-purpose assistant, **hidden until enabled** via Settings → EnableAllAgents.

Guiding principle: **Home is deterministic + button-driven; AI lives only in dedicated topics**.

---

## 2) Telegram Topics model (how it works)
- Each topic == message thread, identified by `message_thread_id`.
- Incoming updates in that topic include `message_thread_id`.
- Outgoing messages must include `message_thread_id` to land in the right topic.
- Conversation key for state/memory: **(chat_id, message_thread_id)**  
  - Home: `threadId = 0`
  - Travel AI: `threadId = TravelAiThreadId`
  - General AI: `threadId = GeneralAiThreadId` (only after enabled)

---

## 3) UX structure

### 3.1 Home keyboard (ReplyKeyboard or a single “Home panel” message with inline buttons)
Before enabling all agents:
- Find flights
- My tracking
- Settings
- Open Travel AI

After enabling:
- Find flights
- My tracking
- Settings
- Open Travel AI
- Open General AI  ✅

### 3.2 Settings screen
A single editable message (avoid spam). Buttons:
- EnableAllAgents (visible only if not enabled)
- DisableAllAgents (optional for v1; can keep out initially)
- Back

**EnableAllAgents behavior**
1) Persist `AgentsEnabled = true` for this chat/user.
2) Create “General AI” topic if missing.
3) Confirm + show button “Open General AI”.

---

## 4) Core user journeys

### 4.1 Home → Find flights (guided, no AI)
1) User taps **Find flights**
2) Wizard collects:
   - From (city/airport)
   - To (city/airport)
   - Dates (e.g., 15–20 March)
   - Optional quick filters: passengers/cabin/stops/max price
3) Bot returns:
   - Top offers list (top 5–10)
   - A **price chart image** (PNG)
   - Inline buttons:
     - ✅ Start tracking (hourly)
     - 🔁 Refine filters
     - 🔙 Back / New search

### 4.2 Start tracking (hourly)
- User taps Start tracking under a search result.
- Bot posts a “Tracking card” message (in Home thread):
  - Route/dates/filters
  - Current min/median price
  - Last checked time
  - Buttons:
    - 🛑 Stop tracking
    - ⚙️ Adjust (frequency, threshold, quiet hours)
    - 📈 Show chart
- Background worker checks **every hour** and posts updates.
- Default notify policy: notify only on min-price change (plus optional heartbeat every 6h).

### 4.3 Stop tracking
- User taps Stop tracking under tracking card.
- Bot:
  - Marks job stopped, cancels schedule
  - Edits the tracking card to “Stopped” and removes stop button.

### 4.4 Travel AI topic
- Normal chat experience with LLM.
- Allowed tools: travel tools (flight search reuse, hotels, itinerary, etc.)
- Can generate actions that post back to Home (e.g., “Track this flight”).

### 4.5 General AI topic (gated)
- Appears only after **EnableAllAgents**.
- General-purpose LLM chat.

---

## 5) Requirements (functional)

### 5.1 Topics lifecycle
- On `/start`:
  - Ensure Travel AI topic exists (optional; can also be created lazily on first open).
  - **Do NOT create General AI topic** unless enabled.
  - Show Home panel.
- On Settings → EnableAllAgents:
  - Create General AI topic (if missing), store threadId.
  - Confirm + show “Open General AI”.

### 5.2 Routing
- `threadId == 0` → HomeFlow only (wizard + tracking management).
- `threadId == TravelAiThreadId` → TravelAI handler.
- `threadId == GeneralAiThreadId` → GeneralAI handler, but only if `AgentsEnabled`.

### 5.3 Flight search
Inputs: from/to/date range/filters.  
Outputs: offers list + queryHash + chart data.  
v1 provider: mock/deterministic; real provider later via adapter.

### 5.4 Tracking
TrackingJob includes:
- queryHash + canonical query params
- frequencyMinutes (default 60)
- quietHours (default 23:00–07:00, local tz)
- threshold policy (optional)
- lastMinPrice/lastMedianPrice
- nextRunAt
- trackingMessageId
- status

### 5.5 Anti-spam
- Default: notify on price change only.
- Quiet hours default enabled.
- Max tracking jobs per user: 10.

---

## 6) Data model (suggested)
- `User { userId, chatId, locale, tz, createdAt }`
- `UserSettings { chatId, agentsEnabled, updatedAt }`
- `TopicRegistry { chatId, travelAiThreadId, generalAiThreadId?, updatedAt }`
- `WizardState { chatId, step, from, to, dates, optionsJson, updatedAt }`
- `TrackingJob { id, chatId, queryHash, queryJson, frequencyMinutes, quietHoursJson, thresholdJson,
                lastMinPrice, lastMedianPrice, lastCheckedAt, nextRunAt, trackingMessageId, status }`
- `AiThreadMemory { chatId, threadId, summary, vectorRef, updatedAt }`

---

## 7) Architecture (implementation-friendly)
**.NET + Telegram.BotAPI** with clean separation:

- Bot.Host
  - Update receiver (webhook or long polling)
  - Router by (chatId, threadId)
  - Background worker host (tracking)
- Bot.Application
  - HomeFlow engine (wizard)
  - Tracking orchestration
  - Topic management service
- Bot.Domain
  - Entities: TrackingJob, UserSettings, WizardState
  - Policies: notification/quiet-hours
- Bot.Infrastructure
  - SQLite persistence
  - Chart renderer
  - Flight provider adapter(s)

---

## 8) Claude Code build prompt (copy/paste)

You are Claude Code working in a mono-repo. Build a production-ready Telegram bot using private-chat Topics.

### Goal
Implement “Travel AI Assistant” with:
- Home thread (threadId=0): deterministic flight wizard + tracking (NO AI).
- Topic “Travel AI”: LLM chat + travel tools.
- Topic “General AI”: LLM chat (general purpose) **hidden until user presses EnableAllAgents** in the bot’s Settings UI.

### Stack constraints
- .NET (latest stable)
- Telegram.BotAPI (latest)
- SQLite storage (EF Core or Dapper)
- Background worker via IHostedService for tracking scheduler
- Clean architecture: transport/application/domain/infrastructure
- Testable interfaces + unit tests for wizard + tracking diff

### Required behaviors

1) Routing
- ConversationKey = (chatId, threadId) where threadId = message.message_thread_id ?? 0.
- threadId == 0 => HomeFlow only.
- threadId == TravelAiThreadId => TravelAI handler.
- threadId == GeneralAiThreadId => GeneralAI handler, but only if UserSettings.AgentsEnabled == true.

2) Settings gating
- Telegram cannot notify about “bot settings” toggles. Implement in-bot Settings:
  - Home has a Settings button.
  - Settings screen has inline button: EnableAllAgents.
  - On EnableAllAgents:
    - Persist AgentsEnabled = true.
    - Create General AI topic via createForumTopic if missing.
    - Store general threadId in TopicRegistry.
    - Send confirmation + navigation button “Open General AI”.

3) Topic setup
- On /start:
  - Ensure Travel AI topic exists (or create lazily when user taps Open Travel AI).
  - Do NOT create General AI topic unless enabled.

4) HomeFlow wizard (NO AI)
- Find flights launches state machine collecting from/to/dates/options.
- Use inline keyboards + callback_data; support manual text entry fallback.
- After search: send offers list + attach chart PNG.
- Under results: “Start tracking (hourly)” button referencing queryHash.

5) Tracking
- On Start tracking:
  - Persist TrackingJob with nextRunAt = now + 60 minutes.
  - Send tracking card message and store trackingMessageId.
- Background worker:
  - Every minute, fetch due jobs and execute.
  - Compare prices; notify on min-price change; respect quiet hours.
  - Update lastCheckedAt + nextRunAt.
- Stop tracking:
  - Mark stopped; remove from schedule; edit tracking card to “Stopped”.

6) Charts
- Generate PNG server-side (SkiaSharp preferred).
- Simple line chart: day/hour vs price.
- Attach as photo with caption summary.

7) Flight provider
- Define IFlightProvider interface.
- Implement MockProvider with deterministic pseudo-prices.
- Keep it pluggable for later real API integration.

### Deliverables
- Working bot code with README:
  - env vars
  - local run instructions
  - db init/migrations
- Project structure:
  - src/Bot.Host
  - src/Bot.Application
  - src/Bot.Domain
  - src/Bot.Infrastructure
  - tests/Bot.Tests
- Idempotent callback handling and topic creation.

### Non-goals
- No payments
- No booking
- No multi-city in HomeFlow

Implement end-to-end with robust callback routing, minimal but solid UX, and clean abstractions.

---

## 9) Callback data schema (recommended)
Keep callback_data short; store full payload server-side.

- `home|find`
- `home|tracking_list`
- `home|settings`
- `settings|enable_all_agents`
- `settings|back`
- `flight|set_from:<code>`
- `flight|set_to:<code>`
- `flight|set_dates:<token>`
- `flight|search:<wizardId>`
- `track|start:<queryHash>`
- `track|stop:<trackingJobId>`
- `nav|open_travel_ai`
- `nav|open_general_ai`

---

## 10) Implementation notes
- Prefer “single panel” messages with edits for Home/Settings to reduce noise.
- Store Topic thread ids per chat to avoid repeated searches.
- Gate any General AI entry points by AgentsEnabled.
