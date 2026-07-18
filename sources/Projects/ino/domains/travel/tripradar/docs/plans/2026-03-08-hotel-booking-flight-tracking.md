# Hotel Booking Link & Flight Price Tracking Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete two Telegram bot features: direct hotel booking links with rich cards, and flight price tracking with Kafka notifications.

**Architecture:** Extend existing TelegramBotFlow wizard with URL buttons for hotels, add SendPhoto to ITelegram for price charts, wire Kafka flight notifications end-to-end, and add tracking management commands.

**Tech Stack:** .NET 11, Orleans 10, Telegram.BotAPI, Confluent.Kafka, System.Text.Json

---

### Task 1: Add URL support to InlineButton and TelegramGrain

**Files:**
- Modify: `src/Assistant/Assistant/Telegram/Models/TelegramModels.cs`
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramGrain.cs`

Add optional `Url` property to `InlineButton`. When `Url` is set, create a Telegram URL button instead of callback button. This unblocks hotel booking links.

### Task 2: Add SendPhoto to ITelegram and TelegramGrain

**Files:**
- Modify: `src/Assistant/Assistant/Telegram/ITelegram.cs`
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramGrain.cs`

Add `SendPhoto(chatId, photoBytes, caption, threadId, ct)` for sending PNG price charts.

### Task 3: Upgrade hotel stays formatting with booking URL buttons

**Files:**
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/TripRadar/TripRadarMessageFormatter.cs`
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs`

Replace plain-text stays output with per-hotel messages containing inline "Book" URL buttons. Each hotel gets its own message card with name, rating, price, and a direct booking link button.

### Task 4: Upgrade flight results formatting with buy URL buttons

**Files:**
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/TripRadar/TripRadarMessageFormatter.cs`
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs`

Each flight result gets a "Buy ticket" URL button. Track price and New search buttons stay below the results.

### Task 5: Add tracking management commands (list/stop)

**Files:**
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/Wizard/WizardFlowService.cs`
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/Wizard/WizardCallbackCodec.cs`
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs`

Add "My trackings" button to main menu. List active trackings with stop buttons. Wire `wiz:action:stop_tracking:{id}` callback to `StopFlightTrackingAsync`.

### Task 6: Wire Kafka flight price alerts end-to-end

**Files:**
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/Notifications/FlightPriceAlertService.cs`
- Modify: `src/Assistant/Assistant.Silo/TelegramBotFlow/TripRadar/TripRadarMessageFormatter.cs`

Enhance price alert messages with inline buttons (buy URL, stop tracking). Add price direction emoji and percentage.

### Task 7: Build and verify

Build Aspire, run, test both features via Telegram bot.
