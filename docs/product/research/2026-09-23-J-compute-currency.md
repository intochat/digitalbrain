# J-compute-currency

## Summary
IntoChat's own docs already describe the Compute idea: a balance island, an estimate plus a maximum approved amount before paid work, and a reserve → execute → meter → settle lifecycle with idempotency keys and standing budgets (docs/intochat-customer-product-design.md:42-55, 92, 106-110). None of it is built. The shell shows a placeholder, 'Compute —' (workspace_islands.dart:253). The runtime keeps only three token counters for the last agent run. It records no price for any model, has no ledger, and has no account or principal to own a wallet: the deployment is single-owner. Paid marketplace apps also assume isolation that does not exist yet. Behaviors run with the host user's privileges, and "approval" today is a sentence in the system prompt, not something the platform enforces.

The industry has settled on a clear pattern for AI compute. The unit is pegged to money, not a floating currency: GitHub AI Credits and Anthropic's Claude Consumption Units (CCUs) are both $0.01, and Cursor, Hugging Face and Replit all bill dollar-denominated usage. Every service is priced per dimension: for LLMs that means input, cache write, cache read and output tokens plus tool fees; for storage it means GB-month and operations. Usage flows through an event pipeline with deterministic idempotency keys and bounded lateness windows (Stripe: 35 days; Metronome: 34-day dedup; AWS and Azure: 24 hours). A credit ledger holds grants, each with a priority, expiry and cost basis. Spend is controlled by caps, budgets and alerts at several scopes, and approvals follow the "allowance" shape from the Agentic Commerce Protocol (max amount, merchant, expiry, single use). GitHub dropped per-request multipliers because "a quick chat question and a multi-hour autonomous coding session can cost the user the same amount". Cursor's June 2025 switch shows how badly unclear pricing changes break trust.

The hard part is not metering. It is letting third-party developers charge users' prepaid balance and then paying those developers. Stripe forbids using billing credits as stored value or for payments to third parties. Azure credits cannot pay publisher fees. Under PSD2, a prepaid instrument that pays many merchants falls outside the limited-network exclusion, and past €1M in 12 months the issuer must notify the regulator. EU consumer authorities (CPC principles, March 2025) require prices in real money, bundle sizes that don't force overbuying, fair expiry terms and withdrawal rights. Apple requires credits bought in-app to use in-app purchase and never expire. The Roblox pattern is the most workable one: purchased currency is never redeemable for cash, developers accrue a separate "earned" balance, and payouts in fiat are gated by identity checks (KYC), tax forms and minimum thresholds.

Recommendation: a fixed peg of 100 Compute = $1, stored internally as integer micro-units. A versioned price book maps each meter dimension (per-model token classes, neuron tariffs such as storage GB-month) to a rate plus margin. An in-house real-time wallet ledger (a per-account grain with an append-only journal) holds grants, reservations and settlement. Approvals become enforced allowances. IntoChat stays merchant of record for every Compute sale, and developers are paid a revenue share from a separate earnings ledger through Stripe Connect. Stripe handles top-up payments and tax. Stripe Billing credits should not be the balance of record, because they only reconcile at invoice time and customers can overspend during the cycle.

## Findings

### J1 (gap, high) Runtime captures only 3 token counters, last-run only, and no model price, so correct rating is impossible
The agent loop adds up UsageDetails across model calls, then keeps only input, output and total tokens (AgentUsage, InferenceUsage). AgentNeuron exposes only the last run's usage. The Microsoft.Extensions.AI UsageDetails type also carries CachedInputTokenCount, ReasoningTokenCount and a summable AdditionalCounts dictionary, and IntoChat drops all three. That breaks pricing: on Anthropic, cache reads cost 0.1x base input, 5-minute cache writes 1.25x and 1-hour writes 2x, web search costs $10 per 1,000 searches, and US-only inference adds a 1.1x multiplier. Mapping IntoChat's input count to a price therefore gives wrong numbers. No LLMModel subclass declares a price. Opus5, for example, has only Id and Provider. There is no persistent usage history to build a statement from.
Evidence: src/Modules/AI/Contracts/Agents/AgentState.cs:6-9; src/Modules/AI/Contracts/Inference/InferenceContracts.cs:46; src/Modules/AI/AI/Inference/InferenceMapping.cs:159-160; src/Modules/AI/AI/Agents/AgentTurnRunner.cs:139-168; src/Modules/AI/AI/Agents/AgentNeuron.cs:236-237; src/Modules/AI/Contracts/LLM/LLMModel.cs:3-44; src/Modules/AI/Contracts/Anthropic/Opus5.cs:3-8; https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.usagedetails.cachedinputtokencount; https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.usagedetails.reasoningtokencount; https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.usagedetails.additionalcounts; https://platform.claude.com/docs/en/about-claude/pricing

### J2 (fact, high) The Compute product design already exists in docs; the UI shows a truthful placeholder
The customer product design already specifies:
- a Compute balance in the account island;
- an estimate and a maximum approved amount for expensive operations;
- one consolidated estimate per chain of operations;
- standing budgets that avoid repeated confirmations;
- atomic reservation with idempotency keys;
- settle and release;
- ambiguous provider results held for reconciliation rather than charged again;
- spend limits per task, automation and workspace;
- opt-in auto-recharge with a cap.
It also names a 'Compute ledger' contract. Local operations must not deduct Compute. The Flutter shell renders 'Compute —' and a test locks that placeholder in. The research below mostly validates this design and adds unit, pricing, marketplace and legal specifics.
Evidence: docs/intochat-customer-product-design.md:42; docs/intochat-customer-product-design.md:51-55; docs/intochat-customer-product-design.md:92; docs/intochat-customer-product-design.md:106-112; docs/intochat-neuron-app-composition.md:116-125; docs/intochat-local-apps-design.md:46; src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_islands.dart:253; src/Modules/Google/Flutter/app/shell/test/local_app_shell_test.dart:42

### J3 (gap, high) LLM spend happens at several call sites; metering belongs in the shared IChatClient pipeline, not in traces
Models are called from AgentTurnRunner, InferenceService, PlaywrightWebAgent and UntrustedContentScreen. That last one is a screening call the user never sees but still pays for. A meter placed in one loop would miss spend. The shared pipeline in AIClients already layers UseFunctionInvocation and UseOpenTelemetry, so a metering DelegatingChatClient is the natural choke point. Non-LLM neuron calls could be metered with Orleans grain call filters, which the docs list for authorization and logging.

OpenTelemetry GenAI attributes such as gen_ai.usage.input_tokens and cache_read.input_tokens are useful for dashboards. They should not be the billing source: traces are sampled and lossy, and the owner already complains about trace noise. Billing needs durable, idempotent meter events.
Evidence: src/Modules/AI/AI/Agents/AgentTurnRunner.cs:147-160; src/Modules/AI/AI/Inference/InferenceService.cs:62-75; src/Modules/AI/AI/Web/PlaywrightWebAgent.cs:126; src/Modules/AI/AI/Clients/UntrustedContentScreen.cs:52; src/Modules/AI/AI/Clients/AIClients.cs:96-102; https://learn.microsoft.com/dotnet/orleans/overview#what-can-be-done-with-orleans; https://opentelemetry.io/docs/specs/semconv/registry/attributes/gen-ai/

### J4 (gap, high) Prerequisites missing: no account principal, no sandbox, approvals are prompt-only
Three prerequisites are missing:
- **Account:** a wallet needs an owner, but the deployment is 'single-owner BasicAuth/local-open', explicitly not multi-tenant, and Kernel has no TenantId, WorkspaceId or PrincipalId.
- **Sandbox:** behavior code 'executes with the host OS user's privileges... not a hostile-code sandbox', and the design says untrusted multi-tenant execution needs OS or container isolation. Third-party marketplace apps that can spend users' money cannot ship on this runtime.
- **Enforced approval:** the only 'approval' today is an instruction in the assistant's system prompt ('Deploy only after explicit approval'). A prompt injection or model error can bypass it. Paid actions need platform-enforced allowances checked outside the model.
Evidence: docs/superpowers/plans/2026-09-20-agent-data-window.md:22; src/Modules/DigitalBrain/Behaviors/README.md:88; docs/superpowers/specs/2026-09-22-programmable-behaviors-design.md:41; src/Modules/AI/AI/ConversationalAgent.cs:114

### J5 (pattern, high) AI platforms converged on money-pegged credits billed per token at API rates, not abstract floating currencies
Examples of the convergence:
- **GitHub:** since June 1, 2026 Copilot consumes GitHub AI Credits, where 1 credit = $0.01, calculated from input, output and cached tokens 'according to the published API rates for each model'. It replaced per-request multipliers because 'a quick chat question and a multi-hour autonomous coding session can cost the user the same amount'. Plans include monthly credits that are forfeited at month end.
- **Anthropic:** marketplace billing uses Claude Consumption Units at $0.01 per CCU; discounts are applied as fewer CCUs, never by changing the CCU price.
- **Cursor:** plans include dollar amounts of usage at API prices, with on-demand usage after that.
- **Hugging Face:** monthly dollar credits, passed through at provider cost with no markup.
- **Replit:** credits spent by effort.
Abstract, floating currencies such as Robux belong to consumer games and create a buy/cash-out spread that EU regulators now scrutinize (see J15).
Evidence: https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/; https://docs.github.com/en/copilot/how-tos/manage-and-track-spending/prepare-for-your-move-to-usage-based-billing; https://platform.claude.com/docs/en/about-claude/pricing; https://cursor.com/help/models-and-usage/usage-limits; https://huggingface.co/docs/inference-providers/pricing; https://docs.replit.com/billing/ai-billing

### J6 (pattern, high) Metering pipelines share one shape: idempotent events, bounded lateness, dimensions, async error reporting
Each vendor follows the same shape with different limits:
- **Stripe meter events:** event_name, customer, value and an optional identifier used for dedup. Timestamps must be within the past 35 days and no more than 5 minutes ahead. Limits are 1,000 calls/s, one concurrent call per customer per meter, and 10k dimension combinations per meter-hour. Errors arrive asynchronously as events.
- **Metronome:** requires transaction_id, customer_id, event_type and timestamp. A duplicate transaction_id within 34 days is ignored. It recommends deterministic IDs, such as node id plus minute bucket.
- **AWS BatchMeterUsage:** dedup per customer/hour/dimension, at most 25 records, rejected 24 hours or more after the event.
- **Azure:** one event per resource/dimension/hour, 24-hour window, 409 on duplicates.
For IntoChat, meter event IDs should be derived from (operationId, meterId, step or attempt). Aggregation and rating happen after ingest, never in the client.
Evidence: https://docs.stripe.com/billing/subscriptions/usage-based/recording-usage-api; https://docs.metronome.com/guides/events/send-usage-events; https://docs.aws.amazon.com/marketplace/latest/APIReference/API_marketplace-metering_BatchMeterUsage.html; https://learn.microsoft.com/partner-center/marketplace-offers/marketplace-metering-service-apis; https://learn.microsoft.com/partner-center/marketplace-offers/marketplace-metering-service-apis-faq

### J7 (fact, high) Stripe Billing credits cannot be IntoChat's real-time balance or pay third parties
Stripe credit grants:
- apply only to metered subscription prices, at invoice finalization;
- per Stripe, credits 'are only reconciled at invoice time. Customers can exceed their balance during the cycle';
- are capped at 100 unused grants per customer;
- are not restored by a credit note (a new grant is needed).
Stripe's prohibited uses include issuing billing credits as stored value and letting customers use them 'for payments to third parties'. Stripe now recommends Metronome for prepaid drawdown and real-time balances, but Metronome shows no Stripe Connect support. IntoChat should therefore keep the authoritative wallet in-house, use Stripe for top-up payments and tax, and use Connect for developer payouts.
Evidence: https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://docs.stripe.com/billing/subscriptions/usage-based/compare-metronome

### J8 (pattern, high) Wallet = append-only ledger of credit grants with priority, expiry, category and cost basis
The vendors model balances the same way:
- **Orb:** credit blocks in an append-only ledger with optional expiry and a per_unit_cost_basis for revenue recognition. It deducts the soonest-expiring block first, then the lower cost basis, so $0 trial credits burn before paid ones.
- **Stripe:** orders grants by priority, then expires_at, then promotional before paid, then effective_at. It separates ledger balance from available balance.
- **Lago:** separates paid_credits (credited only after payment is confirmed) from granted_credits (instant), with up to 5 wallets ranked by priority, optional expiry voiding and threshold top-ups.
These map directly to Compute grant types: MonthlyFree, Promotional, Purchased, Refund and Compensation, each tagged with cost basis. Payouts and refunds then know how much real money backed each unit spent.
Evidence: https://docs.withorb.com/product-catalog/prepurchase; https://docs.withorb.com/product-catalog/credit-systems; https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://getlago.com/docs/guide/wallet-and-prepaid-credits/overview

### J9 (pattern, high) Spend control = hard caps at several scopes, threshold alerts, distinct stop errors, and single-use allowances
Examples of spend control:
- **Anthropic:** tiers carry monthly spend caps ($500 / $1,000 / $200,000) and new organizations start in an Evaluation tier as a fraud control. Hitting a cap returns 429 with error_code enforced_spend_limit_reached and no retry-after; a user-set limit returns 400. Workspaces can have lower limits.
- **GitHub:** budgets at enterprise, cost-center and user level with alerts at 75/90/100%. Older accounts default to a $0 overage budget.
- **Replit:** 'asks for confirmation before a paid action starts', plus usage alerts and hard budget caps.
- **OpenMeter:** metered entitlements for real-time access checks.
- **Agentic Commerce Protocol:** a delegated payment carries an allowance {reason: one_time, max_amount, currency, merchant_id, expires_at} and requires an Idempotency-Key, with 409 on conflicting reuse.
This allowance is the right primitive for 'BackgroundRemover may spend up to X Compute on this request until time T'.
Evidence: https://platform.claude.com/docs/en/api/rate-limits; https://docs.github.com/en/billing/concepts/product-billing/github-copilot-premium-requests; https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/; https://docs.replit.com/billing/ai-billing; https://openmeter.io/docs/billing/entitlements/entitlement; https://developers.openai.com/commerce/specs/payment

### J10 (risk, medium) Effort-based agent pricing erodes trust unless estimate, cap, actual and failure policy are explicit
Replit bills Agent work per 'checkpoint' by effort. Plan Mode is 'billable reasoning even when it does not change code', and usage can take 30 minutes to appear. Third-party reviews (secondary sources) complain that the price is visible only after the debit, including for failed attempts. Cursor's June 2025 move to API-price usage caused surprise bills: it apologized ('not communicated clearly... we take full responsibility'), refunded charges from June 16 to July 4, and added usage dashboards and spend limits. GitHub removed its fallback experiences when included credits run out.

IntoChat's example flows ('show me all customers' makes several model calls plus Supabase tools, with retries) are exactly this kind of multi-step effort. Each user intent needs a pre-run estimate range, a hard cap, the actual cost and a published rule for retried or failed steps. Retries caused by platform errors, such as the 'date' field kind failure, should not be charged to the user.
Evidence: https://docs.replit.com/billing/ai-billing; https://www.banani.co/blog/replit-pricing; https://cursor.com/blog/june-2025-pricing; https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/; src/Modules/AI/AI/Agents/AgentTurnRunner.cs:141-171

### J11 (pattern, high) Neuron tariffs should be declared, immutable dimensions with explicit aggregation, as in Cloudflare and Azure
Reference designs:
- **Cloudflare Workers:** bills requests and CPU-ms, not wall-clock or I/O wait. Durable Objects are the closest analog to Orleans grains and bill requests plus GB-s of in-memory duration.
- **Cloudflare R2:** bills GB-month (the average of each day's peak storage), Class A and B operations, a 30-day minimum for infrequent access, and rounds up to the next unit.
- **Azure SaaS dimensions:** each has an immutable ID, display name, unit of measure, per-plan price and included quantity. They lock after publishing, allow at most 30 per offer and 5 decimal places.
A FileSystem neuron's tariff would then be {storage.gb_month: daily-peak-average, blob.write_ops: sum, blob.read_ops: sum}, each with a unit and aggregation declared in its contract. Prices live in a versioned price book, not in neuron code, so they can change with notice without redeploying.
Evidence: https://developers.cloudflare.com/workers/platform/pricing/; https://developers.cloudflare.com/r2/pricing/; https://learn.microsoft.com/partner-center/marketplace-offers/saas-metered-billing

### J12 (pattern, high) Developer revenue share and payouts: separate 'earned' balance, verification, thresholds, platform as merchant of record
Reference points for revenue share and payouts:
- **Roblox DevEx:** pays only on Earned Robux, never purchased Robux. It requires at least 30,000 Earned Robux, age 13+, a verified email and a W-9/W-8 tax form, and pays through Tipalti. The rate is $0.0038 per Robux, or $0.0054 for spend by US players aged 18+.
- **Store fees:** Microsoft Marketplace charges a 3% store fee and AWS 3% on public SaaS offers.
- **Stripe Connect marketplace:** the platform is merchant of record, handles disputes and refunds, takes application fees, and 'is responsible for covering the negative balances of your connected accounts'.
IntoChat should credit developers into a separate Earned Compute or fiat earnings ledger, net of platform fee. Payouts should run after a holding period that covers the refund and dispute window, with claw-back from unpaid earnings.
Evidence: https://create.roblox.com/docs/production/monetization/developer-exchange; https://learn.microsoft.com/partner-center/marketplace-offers/marketplace-commercial-transaction-capabilities-and-considerations; https://docs.aws.amazon.com/marketplace/latest/userguide/listing-fees.html; https://docs.stripe.com/connect/marketplace

### J13 (fact, high) Platforms keep free or promotional credits away from third-party app charges
Azure: free credits and monetary commitment 'can't be used to pay for publisher software license fees'. Stripe prohibits billing credits for payments to third parties. Hugging Face's monthly credits apply only to requests routed and billed by Hugging Face, not to custom provider keys.

For IntoChat: if free-tier Compute can pay a marketplace app, IntoChat is funding the developer payout from its own margin, and that is a business decision. The safer default is that free and promotional grants cover first-party meters (LLM tokens, storage) only, while marketplace charges draw from purchased grants.
Evidence: https://learn.microsoft.com/partner-center/marketplace-offers/marketplace-commercial-transaction-capabilities-and-considerations; https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://huggingface.co/docs/inference-providers/pricing

### J14 (risk, medium) Stored-value / e-money risk once Compute pays third-party developers
PSD2 Article 3(k) excludes only instruments used within a limited network or for a very limited range of goods and services. EBA Guidelines EBA/GL/2022/02 apply from June 1, 2022, and issuers must notify the regulator once transactions exceed €1M over 12 months.

A prepaid balance spent across many independent developers' apps moves toward a regulated payment or e-money instrument. Stripe also forbids stored-value use. FinCEN treats value that 'substitutes for currency' as convertible virtual currency.

Mitigations taken from Roblox and the app stores:
- IntoChat is merchant of record for every Compute spend; developers are suppliers paid a revenue share.
- Compute cannot be transferred between users.
- Compute cannot be cashed out by consumers.
- There is no peer-to-peer gifting.
This is not legal advice and needs counsel before the marketplace charges money.
Evidence: https://eba.europa.eu/publications-and-media/press-releases/eba-publishes-final-guidelines-limited-network-exclusion; https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://www.fincen.gov/system/files/2019-05/FinCEN%20Guidance%20CVC%20FINAL%20508.pdf; https://en.help.roblox.com/hc/en-us/articles/115004647846-Roblox-Terms-of-Use

### J15 (risk, medium) EU consumer rules for virtual currencies constrain the Compute UX: money shown, no forced overbuy, fair expiry, withdrawal
The CPC Network's Key Principles (March 21, 2025, based on the UCPD, CRD and UCTD) require:
1. Prices in real-world money even when paid in virtual currency.
2. No obscuring of costs through multiple currencies.
3. No forcing consumers to buy more currency than they need (bundle sizing).
4. Pre-contractual information.
5. The withdrawal right within 14 days where applicable.
6. Plain, fair terms, which covers expiry and unilateral changes.
7. Protection of children and vulnerable consumers.
For IntoChat, every estimate and statement shows the money value next to Compute, top-ups allow arbitrary amounts, and expiry must be justified. OpenAI and Anthropic's '1-year expiry, non-refundable' model is set for developer (mostly B2B) customers and is not safe to copy for consumers.
Evidence: https://www.reedsmith.com/articles/qas-on-the-eu-consumer-protection-authorities-joint-guidance-paper/; https://commission.europa.eu/document/download/8af13e88-6540-436c-b137-9853e7fe866a_en?filename=Key+principles+on+in-game+virtual+currencies.pdf; https://support.claude.com/en/articles/8977456-how-do-i-pay-for-my-api-usage; https://help.openai.com/en/articles/8264644-how-can-i-set-up-prepaid-billing

### J16 (risk, high) Mobile app stores: selling Compute in-app requires IAP and non-expiring credits; mini-apps and plug-ins fall under the same rules
Apple guideline 3.1.1: 'credits or in-game currencies purchased via in-app purchase may not expire', and apps need a restore mechanism. Guideline 4.7 covers mini apps, chatbots and plug-ins: that content 'must follow Guideline 3.1' to sell digital goods. US-storefront apps may link to external purchase; other storefronts need entitlements. Google Play requires Play Billing for virtual currency sold in-app. The US alternative billing fee is 25% for non-subscription digital items.

If the Flutter client ships to iOS or Android with in-app top-ups, the Compute expiry policy and per-app pricing must satisfy the stores. Top-ups on web or desktop avoid this.
Evidence: https://developer.apple.com/app-store/review/guidelines/; https://support.google.com/googleplay/android-developer/answer/10281818?hl=en; https://support.google.com/googleplay/android-developer/answer/16497028

### J17 (risk, low) VAT timing depends on voucher classification; general-purpose Compute is likely a multi-purpose voucher
Under EU Directive 2016/1065, a single-purpose voucher is taxed when issued, and only when the place of supply and the VAT due are known at issuance. Otherwise it is a multi-purpose voucher, and VAT is due when it is redeemed. The issuer's conditions decide the classification.

Compute spendable on first-party LLM, storage and third-party apps from different jurisdictions and VAT treatments probably makes it multi-purpose. That means per-consumption tax records, which are one more reason to keep a per-operation ledger. Vendors also disagree on where credits sit relative to tax: Stripe applies grants before tax, while Lago deducts wallet credits from the post-tax subtotal. Confirm with a tax adviser.
Evidence: https://www.legislation.gov.uk/eudr/2016/1065/introduction/data.xht; https://blogs.pwc.de/en/german-tax-and-legal-news/article/251523/prepaid-cards-or-voucher-codes-for-purchase-of-digital-content-in-an-online-shop-are-single-purpose-vouchers/; https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://getlago.com/docs/guide/wallet-and-prepaid-credits/overview

### J18 (risk, medium) Fraud and abuse vectors: stolen-card top-ups, self-dealing developers, backdated usage, model-driven spend
Controls seen elsewhere:
- Anthropic starts new organizations in a low Evaluation tier and raises limits as usage history builds.
- Stripe Connect platforms carry connected accounts' negative balances.
- The Agentic Commerce Protocol carries risk signals such as card_testing.
- Roblox requires a 30k minimum, verification and ToS compliance before any cash-out.
- AWS and Azure reject usage older than 24 hours, which prevents padding bills late.
IntoChat also has AI-specific vectors:
- a developer publishes an app and 'uses' it with stolen-card Compute to launder money into payouts;
- an app over-reports its own usage;
- a prompt injection makes the assistant invoke a paid app.
Mitigations:
- Compute bought with a card becomes payout-eligible only after a dispute holding period.
- Platform-observed meters take precedence over app-reported usage.
- New developers start with velocity limits.
- Paid tool calls are gated by platform-enforced allowances outside the model (see J4).
Evidence: https://platform.claude.com/docs/en/api/rate-limits; https://docs.stripe.com/connect/marketplace; https://developers.openai.com/commerce/specs/payment; https://create.roblox.com/docs/production/monetization/developer-exchange; https://learn.microsoft.com/partner-center/marketplace-offers/marketplace-metering-service-apis-faq; src/Modules/AI/AI/ConversationalAgent.cs:114

### J19 (fact, medium) Orleans offers distributed ACID transactions and call filters that fit reserve/settle and metering
Orleans transactions (ITransactionalState<T>, [Transaction(TransactionOption.Create/Join)], and ITransactionClient.RunTransaction) give serializable ACID across grains. The official example is an account Withdraw/Deposit transfer, which matches 'debit wallet, credit developer earnings'. Constraints: transactional grains must be [Reentrant], and a transactional state storage is needed. Grain call filters are documented for cross-cutting authorization and telemetry, which suits allowance checks and non-LLM metering. A simpler and very common alternative is a single-writer WalletGrain per account with an append-only journal and per-operation idempotency keys. That avoids distributed transactions on the hot path, with asynchronous transfer to earnings. The repo uses call filters today only in tests.
Evidence: https://learn.microsoft.com/dotnet/orleans/grains/transactions; https://learn.microsoft.com/dotnet/orleans/migration-guide#transaction-client; https://learn.microsoft.com/dotnet/orleans/overview#what-can-be-done-with-orleans; src/Modules/Time/Tests/Unit/Reminders/ReminderTestSupport.cs:10

### J20 (recommendation, medium) Recommended Compute model for IntoChat
1. **Unit:** a fixed peg of 100 Compute = $1, the same as Anthropic CCU and GitHub AI Credits. Store integer micro-Compute (1e-6) in the ledger and round only on statements. Always show the money equivalent. Never float the exchange rate.
2. **Price book:** versioned with effective dates. Each rate is keyed by (meterId, dimension). For LLMs: per model, input, cache-write-5m, cache-write-1h, cache-read, output (reasoning is billed as output), plus tool fees (search per 1k) and a geo multiplier. Each rate is the provider list price times a published margin, which can be zero like Hugging Face. Neuron and app tariffs are declared dimensions (J11) with prices set by developers, then reviewed and locked.
3. **Grants:** a monthly free grant, expiring monthly and valid on first-party meters only. Purchased grants do not expire, or expire only after a long, notified period: this stays Apple-compatible and meets the EU CPC principles. Promotional grants have an explicit expiry. Refund and compensation grants are recorded as new ledger entries.
4. **Lifecycle:** estimate → allowance → reserve (hold) → execute → meter events → rate → settle and release. Retries caused by platform failures are not charged. An ambiguous provider result stays pending reconciliation.
5. **Approvals:** three levels.
   - Standing budget: auto-approve first-party work below a per-request threshold.
   - App install consent: shows the price list and sets a per-app monthly budget.
   - Per-operation allowance {app, operation, maxCompute, expiresAt, single-use} when the estimate exceeds a threshold or budget. The platform enforces it, never the model.
6. **Caps and alerts:** caps per account, workspace, app and automation run, with alerts at 75/90/100%. A hard stop returns an error distinct from rate limiting.
7. **Statements:** grouped per user intent or turn, broken down into component lines (model calls, tools, app charges), each showing Compute and money, estimate versus actual.
8. **Marketplace:** IntoChat is merchant of record. Developer earnings go to a separate ledger net of platform fee. Payouts in fiat run through Stripe Connect after the holding period, with KYC and a minimum. Purchased Compute is never redeemable for cash and cannot move between users.
Evidence: https://platform.claude.com/docs/en/about-claude/pricing; https://docs.github.com/en/copilot/how-tos/manage-and-track-spending/prepare-for-your-move-to-usage-based-billing; https://developers.openai.com/commerce/specs/payment; https://docs.withorb.com/product-catalog/prepurchase; https://developer.apple.com/app-store/review/guidelines/; docs/intochat-customer-product-design.md:106-110

### J21 (recommendation, medium) Minimal platform capabilities, in dependency order
0. **Account principal:** a user or workspace identity that owns a wallet. This is a hard prerequisite (J4).
1. **Meter:** a MeterEvent {id deterministic from operationId+meter+step, accountId, appId?, meterId, dimension, quantity, occurredAt} emitted by an IChatClient metering middleware and a grain call filter. Record every UsageDetails field, including CachedInputTokenCount, ReasoningTokenCount and AdditionalCounts. Use a dedup window of 30+ days and reject events outside a bounded lateness window.
2. **Price book:** versioned rates per dimension. Model prices move from code constants into data. Developer tariffs with immutable dimension IDs.
3. **Wallet/ledger:** an append-only journal holding grants with type, priority, expiry and cost basis, plus holds, settle, release and compensating refunds. It is authoritative and real-time: never the browser, never Stripe invoice-time credits.
4. **Quotas and budgets:** at account, workspace, app and automation-run scope, with alerts and a hard stop.
5. **Approvals:** allowance objects checked by the platform before paid tool or app calls.
6. **Statements:** per-operation line items with the money equivalent, exportable.
7. **Top-up payments:** Stripe Checkout with Stripe Tax; auto-recharge is opt-in with a cap.
8. **Payouts:** earnings ledger, holding period, KYC and tax forms, and a minimum threshold via Stripe Connect.

Defer until needed: enterprise postpaid invoicing, commits and ramps, multi-currency pricing, and buying Metronome, Orb or Lago. Self-hosting OpenMeter or Lago is an option if the in-house ledger becomes a burden.
Evidence: src/Modules/AI/AI/Clients/AIClients.cs:96-102; src/Modules/AI/AI/Inference/InferenceMapping.cs:159-160; https://docs.stripe.com/billing/subscriptions/usage-based/compare-metronome; https://docs.stripe.com/connect/marketplace; https://openmeter.io/docs/billing/entitlements/entitlement; https://getlago.com/docs/guide/wallet-and-prepaid-credits/overview; docs/intochat-customer-product-design.md:92

### J22 (pattern, medium) Refunds and disputes are handled as compensating ledger entries and narrow exception policies
How vendors handle refunds:
- **OpenAI:** prepaid credits expire after 1 year and are non-refundable except for verified billing errors, unauthorized use or service failure.
- **Anthropic:** credits expire after 1 year; 'All credit purchases are non-refundable'; API access stops at a zero balance.
- **Stripe:** voiding an invoice reinstates applied credits, and they expire at once if the grant is past its expiry. A credit note does not restore grants; a new grant is required.
- **Roblox:** Robux are non-refundable, but paid access bought in local currency can be refunded within 48 hours.
For IntoChat, refunds to a user are new Refund grants, never ledger edits. The matching developer earnings are clawed back while still inside the holding period. Card chargebacks freeze the account's grants bought with that card. The published policy must also satisfy EU withdrawal rights (J15).
Evidence: https://help.openai.com/en/articles/8264644-how-can-i-set-up-prepaid-billing; https://support.claude.com/en/articles/8977456-how-do-i-pay-for-my-api-usage; https://docs.stripe.com/billing/subscriptions/usage-based/billing-credits; https://en.help.roblox.com/hc/en-us/articles/115004647846-Roblox-Terms-of-Use


## Open questions
- Compute peg: 100 Compute = $1 (parity with Anthropic CCU and GitHub AI Credits), or another denomination? Is a fixed money peg acceptable, as opposed to an abstract floating currency?
- LLM margin policy: pass provider list prices through at cost (like Hugging Face), charge a flat published margin, or set a per-model margin?
- Purchased Compute expiry: never (Apple IAP-compatible, safest under the EU CPC principles), or 12 months like OpenAI and Anthropic? Should the free monthly grant expire monthly?
- Can free or promotional Compute pay for marketplace apps? If yes, IntoChat funds developer payouts from its own margin.
- Revenue share for app developers: about 3% (Microsoft or AWS marketplace style), 15-30% (app-store style), or a Roblox-like spread? Can non-programmer creators get cash payouts, or only verified (KYC) developers?
- Who is merchant of record for marketplace app charges? The recommendation is IntoChat, with developers as suppliers. Is IntoChat willing to take on the dispute, refund and tax liability that comes with it?
- Charging policy for failed, retried or cancelled multi-step work: charge the provider cost already incurred, charge nothing on platform-caused failures, or charge per completed checkpoint (Replit style)?
- Launch markets and channels: EU consumers at launch? Will Compute be sold inside iOS or Android apps, which forces IAP and non-expiring credits, or only on web or desktop?
- Prepaid only, or also postpaid monthly invoicing for business workspaces (Anthropic offers this by arrangement)?
- Identity prerequisite: are you willing to add real account and workspace principals now? A wallet cannot exist in the current single-owner model.