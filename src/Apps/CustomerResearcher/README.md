# Customer Researcher

Run the IntoChat AppHost (`aspire start --apphost src/Applications/IntoChat/AppHost/IntoChat.AppHost.csproj --non-interactive`), then open **Applications → Customer Researcher** in the Windows desktop client. Enter a company name, optionally including its location or official website, and choose **Research**. The second row shows the actual browser page used by the agent. **Stop** cancels the active request; submitting a replacement cancels the previous request.

The desktop and backend must run on the same Windows host: the backend attaches to the desktop-owned WebView2 page through its loopback debugging port and unique session marker. Each application window has an independent scope and page. No separate browser is launched by this app. Other client platforms show an unsupported message.

The app uses the existing registered `Microsoft.Extensions.AI.IChatClient`, including its configured tool-calling pipeline. AppHost currently selects `IGemma4` as the default model through the existing AI module configuration. Configure the host's normal AI provider/default-model settings and credentials to change this; there is no separate Customer Researcher model setting. The app starts a visible search (or opens a supplied URL), then asks the model to select numbered links actually observed on the page. It executes up to three navigation decisions through this window's Playwright session. Each decision has a fresh, bounded context; it does not depend on the model voluntarily making native tool calls. A separate JSON extraction step uses the observed source pages, prioritizing contact evidence.

The model collects company name, official website, location/address, public email, phone, industry and a short summary. The application verifies each populated value against the actual text of an observed page on the selected official website and builds its source excerpt itself. Unsupported fields remain null. Ambiguous identity asks for a location or website and does not save. A failed research or database operation shows a failure status, never **Saved**.

AppHost provisions the Postgres module's managed `customer-research` database. The typed repository creates `customer_research` idempotently with columns `workspace`, `research_id`, `company_name`, `website`, `location`, `email`, `phone`, `industry`, `summary`, `evidence` (JSONB) and `updated_at`. The primary key is `(workspace, research_id)`. Research IDs are stable for a window and exact trimmed query, so retries update one record. Values use Npgsql parameters; the general-purpose `IPostgres.Query` remains read-only. The application does not persist browser endpoints or handles.

Run focused tests with:

```powershell
dotnet test --project src/Apps/CustomerResearcher/Tests/Unit/DigitalBrain.Apps.CustomerResearcher.Tests.Unit.csproj
```

Set `CUSTOMER_RESEARCH_TEST_POSTGRES` to an Npgsql connection string for a disposable PostgreSQL database to enable the live upsert/isolation test. It creates the table and removes its uniquely scoped test rows. Without that variable the live test is explicitly skipped; controlled in-process tests still cover evidence, ambiguity, saving, failure, retries, workspace isolation and stale-result cancellation.

Browser connections are transient. Attached application activations are kept alive until browser disconnect. After a backend restart, close and reopen the window so it can publish a new connection.
