# Companion AI — Issue Dependencies Map

## Legend

- `→` means "blocks" (left blocks right)
- `IAW-XX` = issue in InteractiveAgents/IAW repo
- `TR-XX` = issue in RoseXTechnology/TripRadar repo

## Dependency Graph

```
IAW REPO                              TRIPRADAR REPO
────────                              ──────────────

Phase 1 (IAW Readiness)               Phase 2 (Foundation)
┌──────────┐
│ IAW-01   │──────────────────────────→ TR-01 (Add NuGet pkgs)
│ NuGet CI │                               │
└──────────┘                               ▼
┌──────────┐                           TR-02 (Agents project)
│ IAW-02   │                               │
│ Custom   │──────────────────────────→    ▼
│ agent doc│                           TR-03 (Agents.Host silo)
└──────────┘                               │
┌──────────┐                               ▼
│ IAW-03   │──────────────────────────→ TR-04 (Wire into Aspire)
│ Discovery│                               │
└──────────┘                          ┌────┴────────────┐
┌──────────┐                          ▼                 ▼
│ IAW-04   │                      TR-05 (Bot         TR-06 (Qdrant)
│ Stable   │                      as client)
│ interfaces│                         │
└──────────┘                          │
                                      │
Phase 2 (IAW Features)                │  Phase 3 (Core Agents)
                                      │
                                      ├──→ TR-07 (UserPreferenceAgent)
                                      │        │
                                      │        ├──→ TR-08 (FlightSearchAgent)
                                      │        │        │
                                      │        ├──→ TR-09 (HotelSearchAgent)
                                      │        │
                                      │        └──→ TR-10 (PlaceDiscoveryAgent)
                                      │
                                      ├──→ TR-11 (Telegram conversation)
                                      │    [needs TR-05 + TR-08]
                                      │
                                      └──→ TR-12 (MiniApp Companion page)
                                           [needs TR-05 + TR-08]

┌──────────┐                          Phase 4 (Vision & Budget)
│ IAW-05   │
│ Vision   │──────────────────────────→ TR-13 (CurrencyAgent + vision)
│ tier     │                               │
└──────────┘                               ▼
                                       TR-14 (BudgetAgent + receipts)
                                           │
┌──────────┐                               ▼
│ IAW-08   │                           TR-18 (MiniApp Money features)
│ Proactive│
│ patterns │──────────────────────────→ TR-15 (WeatherMonitorAgent)
└──────────┘──────────────────────────→ TR-17 (TripTimelineAgent)

┌──────────┐
│ IAW-06   │
│ Kafka    │──────────────────────────→ TR-16 (PriceWatchAgent)
│ bridge   │
└──────────┘

                                       Phase 5 (Deep Intelligence)

                                       TR-19 (TripContextAgent)
                                           │
                                       TR-20 (EventDiscoveryAgent)
                                           │ [needs TR-07 + TR-19]
                                           │
                                       TR-21 (ItineraryPlannerAgent)
                                           │ [needs TR-10 + TR-20 + TR-07]
                                           │
                                       TR-22 (Proactive recommendations)
                                           │ [needs TR-07 + TR-10 + TR-19]
                                           │
┌──────────┐                           TR-23 (Token accounting)
│ IAW-09   │──────────────────────────→    │ [needs TR-04 + IAW-09]
│ Token    │
│ budgets  │
└──────────┘
```

## Critical Path

The minimum path to a working conversational flight search:

```
IAW-01 → TR-01 → TR-02 → TR-03 → TR-04 → TR-07 → TR-08 → TR-11
  │                                  │
  └─ IAW-02, IAW-03 (parallel)       └─ TR-05 (parallel with TR-07)
```

**Estimated critical path:** IAW-01 is the single biggest blocker. Once NuGet packages are published, TripRadar foundation work (TR-01 through TR-05) can proceed rapidly.

## Parallel Work Streams

Once TR-04 (Aspire wiring) is done, these can proceed in parallel:

| Stream | Issues | Dependencies |
|--------|--------|--------------|
| **Core agents** | TR-07, TR-08, TR-09, TR-10 | TR-04 only |
| **Bot integration** | TR-05, TR-11 | TR-04 + one agent |
| **MiniApp** | TR-12 | TR-05 + one agent |
| **Vision agents** | TR-13, TR-14 | TR-04 + IAW-05 |
| **Proactive agents** | TR-15, TR-16, TR-17 | TR-04 + IAW-06/08 |
| **Deep intelligence** | TR-19, TR-20, TR-21 | Core agents done |

## IAW Issues — Independent of TripRadar

These IAW issues have no TripRadar dependency and can be worked on at any time:

- IAW-01: NuGet publishing (CRITICAL — unblocks everything)
- IAW-02: Custom agent docs
- IAW-03: Discovery verification
- IAW-04: Stable interfaces
- IAW-05: Vision tier support
- IAW-06: Kafka bridge
- IAW-07: HTTP tool base
- IAW-08: Proactive agent docs
- IAW-09: Token budgets
- IAW-10: Agent streaming
- IAW-11: Multi-user isolation
- IAW-12: RAG docs
