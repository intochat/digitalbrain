# Telegram reminders design and implementation plan

**Goal:** Add a typed Telegram module, Flutter Mini App, reusable Notification neuron/widget, and one saved behavior connecting incoming private bot messages to durable reminders and notifications.

**Architecture:** Telegram adapts authenticated provider receipts into `IBot.MessageReceived`. A persisted behavior branches to an incoming-message Notification and a tool-free structured decision. Reminder decisions call Time's `IReminders.Schedule`; the reminder aggregate allocates one existing ITimer per reminder. Its `ReminderDue` output flows through the same behavior to Notification. Ambiguous decisions produce a clarification notification. Flutter presents these durable records and cancel/dismiss actions.

## Decisions

- DigitalBrain `v2` stays the main project; preserve all current uncommitted work.
- Reference ino's same-origin Flutter web Mini App and bot launch button design, without copying its parallel host/runtime or weakening DigitalBrain auth.
- Private chats only initially. Verify webhook secret and Mini App signed initData; derive user scope from verified identity, never client-selected neuron names. Bot secrets stay server-side. Empty bot configuration disables Telegram entry points.
- Treat incoming messages to the bot as the event. Message update IDs are stable dedup identities. Existing runtime records accepted work before acknowledging webhook success.
- Use configured timezone (UTC default), provider message timestamp and explicit clock in the semantic input. Missing/ambiguous time produces clarification, not a guessed alarm. No recurring schedule in this iteration.
- `INotification.Publish(PublishNotification(Id, EventId, Title, Message, Kind))`, `Dismiss(DismissNotification(Id, EventId))`, `Read()` maintain a bounded durable inbox. Names `notification:telegram-{userId}`; fields EventId, Title, Message, Kind, CreatedUnixSeconds, Dismissed.
- `IReminders.Schedule(SetReminder(Id, ReminderId, Text, DueUnixSeconds))`, `Cancel(CancelReminder(Id, ReminderId))`, `Read()`. Names `reminders:telegram-{userId}`. ReminderDue payload ReminderId, Text, DueUnixSeconds. Unique timer per reminder; duplicate commands/events cannot create multiple alarms. Future timestamps bounded to one year; no silent overwrite of existing ID.
- `IBot.Accept(TelegramMessage(Id, EventId, UserId, Text, SentUnixSeconds, TimeZone))`; name `telegram:{userId}`. Shared MessageReceived payload EventId/UserId/Text/SentUnixSeconds/TimeZone in camel case. Private-chat webhook and `/start` Mini App link; no external setup executed during development.
- One fixed behavior definition per user, configured before ingress: incoming message → notification; message → structured decision → reminder filter → map → schedule; reminder source → map → due notification; clarification decision → map → clarification notification. Every decision is schema checked and cannot call tools. Other messages create only the incoming notification.
- Mini App endpoints under `/telegram/miniapp`: GET state -> {notifications,reminders}; POST notifications/dismiss {eventId}; POST reminders/cancel {reminderId}. Authorization header carries `tma <initData>`. Static bundle `/telegram/app/`. Do not grant Telegram identity generic DigitalBrain access.

## Implementation tasks

- [x] Time module: durable reminder aggregate over existing ITimer, registered ReminderDue contract, concurrent reminders/cancel/restart/dedup tests.
- [x] UI module: Notification contracts/neuron, reusable Flutter UiNotificationCard, state/dismiss/dedup and widget tests.
- [x] Telegram module: contracts/source, authenticated webhook and Mini App adapters, same-origin Flutter app, provider/auth and client tests.
- [x] Integration: saved behavior factory and lifecycle setup, schema-aware mappings, deployments/module references, end-to-end scripted decision flow including non-reminder and clarification.
- [x] Verify full backend, Flutter packages, Mini App web build; review auth, durability and behavior compatibility; document runnable example and provider prerequisites.

## Acceptance scenarios

1. “Remind me in ten minutes to call Alice”: one inbox message, one scheduled reminder, one due notification.
2. Two reminders coexist with independent timers; cancel one leaves the other intact.
3. Duplicate webhook and restart do not duplicate messages/reminders/notifications.
4. Ordinary conversation never schedules an alarm; “remind me to call Alice” produces a clarification notification.
5. Forged/stale initData, invalid webhook secret and cross-user requests are rejected.
6. Existing behavior, time, UI and assistant regressions remain green.
