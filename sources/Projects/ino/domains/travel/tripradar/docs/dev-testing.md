# Dev Testing Guide

## Dev Login

When the MiniApp runs outside Telegram (no `Telegram.WebApp` detected), it shows a dev login page at `/auth` with preset test users.

### Preset Users

| Card | Telegram ID | Tier | What you can test |
|------|------------|------|-------------------|
| Free User | 100001 | Basic | Flight/hotel search, filters, results browsing |
| Essential User | 100002 | Essential | All Basic features + price tracking, scheduled queries, alerts |
| Advanced User | 100003 | Advanced | All Essential features + higher token limits |

Tap a card to log in instantly. No Telegram account needed.

### Custom Login

Click "Show custom login" below the preset cards to access:
- **User ID** — any numeric Telegram user ID
- **Tier dropdown** — Basic (free), Essential, or Advanced

This is useful for:
- Testing with a specific user ID
- Switching an existing user between tiers (the tier is applied on every login)
- Creating additional test users beyond the presets

### How It Works

The dev login calls `POST /api/v1/tokens/dev` with:
```json
{ "telegramUserId": 100002, "tier": "essential" }
```

The endpoint (development-only, returns 404 in production):
1. Creates or finds a user by Telegram ID
2. Sets the user's tier to the requested level
3. For paid tiers (Essential/Advanced): creates an active subscription with no expiration
4. Returns JWT access + refresh tokens

### Testing Paid Features

**Price Tracking:**
1. Log in as **Essential User** or **Advanced User**
2. Search for flights (e.g., JFK → LAX, one-way, Apr 15)
3. On the results page, tap the **Track** button in the header
4. Select a schedule (Every hour / 6h / 12h / Daily)
5. Navigate to the **Alerts** tab to see the tracking

**Switching Tiers:**
To test how the app behaves when a user's tier changes:
1. Log in as Essential User (ID 100002)
2. Create a price tracking
3. Log out / clear session
4. Log in with the same ID 100002 but select "Basic" tier via custom login
5. Verify tracking creation is now blocked (400 InsufficientSubscriptionTier)
