# Twitter/X receipt source

`twitter:elonmusk` is a reusable durable source neuron. Its name identifies the account; author comparison ignores case and an optional leading `@`. Saved behaviors subscribe to its `Posted` output through the shared behavior catalog and existing synapses. Several behaviors can share the same source.

`ITwitter.Accept(Post)` accepts `{ id, providerPostId, author, text }`. Use a fresh command ID for a fresh receipt. The command goes through the normal typed command descriptor, command journal, scheduled reaction, and durable announcement mechanism. The receipt reports accepted work, not delivery completion. Read the source snapshot or journals to observe completion.

The `Posted` JSON contract is `{ "eventId": "provider post ID", "author": "elonmusk", "text": "post text" }`. The provider post ID is preserved as the event ID across retries; author is canonicalized to the source account. A mismatched author is rejected before scheduling work. IDs, authors and text are bounded to 256, 64 and 25000 characters respectively.

Duplicate command IDs use native command receipt replay. Separately, the source persists the latest **4096 distinct provider post IDs per account**, in insertion order, alongside its snapshot. Repeated provider IDs do not announce again, even with different command IDs or after cold restart. Duplicates do not refresh retention position. The oldest ID is evicted when the next distinct ID exceeds the limit; a redelivery of an evicted ID is treated as new. This is a bounded receipt deduplication policy, not an unlimited historical guarantee. Original post content wins within the retention window.

The deployed silo references the module and contracts, making the grain and generated descriptors available through Orleans discovery. `TwitterModule` registers its generated `Posted` output schema with the behavior catalog. The Aspire AppHost, Docker image and container publish profile all include the module in their manifests. It has no external provider dependencies or provider setup.

## Provider boundary

No live X credentials, webhook, polling service or Twitter API integration is configured. A trusted integration or an operator can simulate a provider receipt through the **existing authenticated describe/call HTTP or MCP surface**: describe the `twitter` neuron, then call its `accept` command for account `elonmusk` using the discovered parameter schema. Existing caller authorization remains in force; there is no new ingress endpoint. The source never verifies a receipt against X, so callers must only submit trusted receipts in production.

`TwitterFacts` covers command receipt replay, reaction output, independent provider deduplication, account mismatch and cold storage reload.
