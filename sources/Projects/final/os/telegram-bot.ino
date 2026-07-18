name: telegram-bot
version: 0.1.0
desc: Telegram bot integration as neurons/synapses experience. Demo surface launches the existing draggable floating Mail (gmail-senders-chart) as TG WebApp / mini-app. Future real bot API calls with grants.
emits: UiSurface, BeginTelegramConnect
triggers: BeginTelegramConnect, TelegramBotLaunch

# Minimal expressive .ino (BDD style) per plan. Rules drive the UI; behavior (real TG http) lives in TelegramConnectorNeuron.
on: BeginTelegramConnect, TelegramBotLaunch:
  show card "Telegram Bot (demo)"
    column(
      text("Connect TG bot (grant for token). Demo only — opens existing Mail floating experience inside TG WebApp context."),
      button("Launch Mail in TG WebApp", BeginTelegramConnect())
    )

# Result/launch produces the link surface. Hyperlink uses the kernel-served Flutter (tg=1 param for future minimal chrome; reuses the OpenWindow/Mail floating already wired).
on: BeginTelegramConnect as _:
  show card "TG WebApp Ready"
    column(
      text("This would call Telegram Bot API (api.telegram.org) with vault token + grants."),
      text("Opens the current DigitalBrain client (with draggable Mail windows) as Telegram WebApp."),
      hyperlink("Open Mail as TG WebApp (floating)", "http://localhost:8080/flutter?tg=1&exp=gmail-senders-chart&mode=floating")
    )
