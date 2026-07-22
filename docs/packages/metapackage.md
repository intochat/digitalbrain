---
title: DigitalBrain
---

# DigitalBrain

The consumer metapackage carries no assembly. It pulls in:

- [`DigitalBrain.Abstractions`](/packages/abstractions)
- [`DigitalBrain.Client`](/packages/client)
- [`DigitalBrain.Aspire`](/packages/aspire)

It does **not** reference [`DigitalBrain.Kernel`](/packages/kernel) or any domain module. A client
chooses only the contract packages it needs; a silo chooses runtime modules separately.
