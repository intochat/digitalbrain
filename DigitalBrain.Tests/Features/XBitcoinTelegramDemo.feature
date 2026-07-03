Feature: X post triggers a Bitcoin price alert on Telegram
	As a user
	I want DigitalBrain to react to a new X post from a watched author
	So that it checks the Bitcoin price and sends me a Telegram alert

	@distribution @e2e @xbitcoindemo
	Scenario: X post from watched author triggers a Bitcoin price alert on Telegram
		Given the X-Bitcoin-Telegram demo pack is installed
		And the egress bus is watching "TelegramReplyRequested"
		When a simulated X post from "elon" arrives for chat 7 with text "big news"
		Then a "TelegramReplyRequested" reply for chat 7 with text "New post from elon. Bitcoin price right now: $61,234.56" reaches the egress bus
