# L-planning-practices

## Summary
This stream looked at how mature product organisations structure planning documents and backlogs, and turned that into a proposed shape for highlevel.md and an epics/ folder in the IntoChat repo. The primary sources largely agree. First, a short narrative written from the customer's side: Amazon's PR/FAQ, with a press release of one page at most and an FAQ of five pages at most, split into external and internal questions. Second, a vision that looks 2-5 years ahead: SVPG, and Pichler's Vision Board with 3-5 standout features. Third, discovery that separates outcomes, opportunities and solutions (Torres's Opportunity Solution Tree, and JTBD job stories). Fourth, a stable map of what the business does, independent of how it is built (the TOGAF business capability map). Fifth, a hierarchy with no more than 3-4 levels where each level has a set purpose: Azure DevOps Epic → Feature → Story/PBI → Task, linked only between different types. SAFe adds the fields that make epics and features checkable: the epic hypothesis statement, business outcomes, leading indicators, NFRs, MVP and a lean business case; for features, a benefit hypothesis plus acceptance criteria. INVEST, the 3Cs, Gherkin (3-5 steps, behaviour not implementation), EARS, and a Definition of Done make work items testable. The Scrum Guide does not define a Definition of Ready, and Scrum.org warns it can turn into a stage gate.

For a platform plus marketplace with an AI assistant, the most useful additions are two. The platform "core interaction" (participants, value unit, filter) comes from secondary summaries of Parker/Van Alstyne/Choudary, so confidence is medium. The other is a capability map with a maturity score per capability. Together they put the owner's scattered ideas into one frame: the SDK type system, vector discovery of neurons, the Compute currency and tariffs, and the app package format. The owner's pain points (a hard-to-understand flow, the failed date field, noisy traces) belong in the tree as opportunities. The owner's ideas (ValueNeuron<string>, NuGet-like packages, Reqnroll discovery, per-neuron tariffs) belong as candidate solutions, each weighed against alternatives, not decided up front. Open architecture questions, such as whether every neuron should be a durable grain, should be written as ADRs (Nygard/MADR) and linked from epics, not settled inside them.

The repo already has planning conventions that the new layer should link to, not replace. These are CONTEXT.md (the domain glossary), date-prefixed docs/superpowers/specs and plans, and docs/intochat-customer-product-design.md, which already covers Compute, custom apps and acceptance examples. What the repo lacks is IDs, frontmatter and a hierarchy. Status is written as prose in only 7 of 40 docs. Plan checkboxes are not a reliable status: two plans have 56 and 45 unchecked boxes and no checked ones, although other docs say they were implemented or verified. Also, Reqnroll .feature files have been added and deleted more than once (26 were removed on 2026-09-17), so Gherkin acceptance criteria should first live in markdown work items, independent of any test framework.

The findings end with concrete recommendations. For highlevel.md: a 16-section outline with a PR/FAQ, a vision board, the platform core interaction, principles, the opportunity tree, a 13-area capability heat map, walking-skeleton journeys, a Now/Next/Later roadmap and WSJF scores. For epics/: EP-/FT-/WI- IDs that are never reused, one folder per epic and per feature, and YAML frontmatter that mirrors Azure DevOps fields and KEP-style status and link fields, so items can later be imported into Boards/Jira. Last come short templates for epics, features and work items, and a lint check for traceability.

## Findings

### L1 (fact, high) Working Backwards PR/FAQ: a press release of one page at most plus an FAQ of five pages at most, split into external and internal
Amazon's PR/FAQ starts from the customer experience. The press release is 'limited to a few paragraphs, always under one page', and the idea must be 'meaningfully better (faster, easier, cheaper)'. The FAQ is at most five pages and includes 'a clear-eyed and thorough assessment of how expensive and challenging it will be… to build'.

PR sections:
- Heading (product name)
- Subheading (customer and benefit, one sentence)
- Summary paragraph (city, outlet, launch date)
- Problem paragraph
- Solution paragraph(s)
- Quotes and 'how easy it is to get started'

The external FAQ covers price, how it works, support and where to buy. The internal FAQ covers competition, market size, technical challenges, financials, regulation, assumptions, and 'What are the top three reasons this product will not succeed?'

For IntoChat: open highlevel.md with a one-page press release for the 'IntoChat OS + marketplace' launch. External FAQ examples: 'How much Compute does LeadGenerator cost?', 'Is my password safe?'. Internal FAQ examples: the durable-grain question, pricing model, trace noise. This gives the owner a way to make 'the product is hard to understand' concrete and testable.
Evidence: https://www.aboutamazon.com/news/workplace/an-insider-look-at-amazons-culture-and-processes; https://workingbackwards.com/resources/working-backwards-pr-faq/; https://workingbackwards.com/concepts/working-backwards-pr-faq-process/

### L2 (fact, high) Vision looks 2-5 years ahead and persuades; strategy changes yearly or quarterly; the Vision Board limits standout features to 3-5
SVPG/Cagan: 'The product vision should describe the desired end state 2-5 years out for software companies.' It 'is not in any sense a spec; it's really a persuasive piece'. 'The product strategy changes frequently (at least yearly if not quarterly)… the product vision doesn't usually change much.'

Pichler's Product Vision Board has five boxes:
- Vision: 'overarching goal… positive change'
- Target Group: 'who its users and its customers are'
- Needs: 'main problem… or primary benefit'
- Product: 'the three to five features that make your product stand out'
- Business Goals: 'why it's worthwhile… to invest'

The extended board adds competitors, revenue sources, cost factors and channels. Pichler says to keep entries brief and leave granular detail to the backlog.

For a multi-sided platform, list each side as its own target group:
- end users (chat/workspace)
- non-programmer app creators
- developers / neuron authors

The extended board's revenue and cost boxes are where Compute belongs.
Evidence: https://www.svpg.com/product-vision-faq/; https://www.svpg.com/vision-vs-strategy/; https://www.romanpichler.com/blog/the-product-vision-board/; https://www.romanpichler.com/tools/product-vision-board/

### L3 (fact, medium) Platform design starts from one 'core interaction' (participants, value unit, filter) plus pull/facilitate/match
Parker, Van Alstyne and Choudary (Platform Revolution) say platform design begins with one core interaction. It has three elements:
- participants (producers and consumers)
- the value unit (the information that lets a consumer decide, e.g. the price and description on eBay)
- the filter (search or matching)

The platform must pull, facilitate and match.

For the IntoChat marketplace:
- Producers are developers and non-programmer creators; consumers are users and the assistant acting for them.
- The value unit is the app listing: what it does (capabilities / neuron interfaces / acceptance examples), permissions, and tariff in Compute.
- The filter is the assistant's vector search over apps, neurons and modules (the owner's Qdrant idea).
- 'Facilitate' includes approving a Compute charge before use (the BackgroundRemover example).

This gives highlevel.md one frame that connects the owner's discovery, packaging and Compute ideas. Confidence is medium because only secondary book summaries were available (the book is not freely accessible).
Evidence: https://blas.com/platform-revolution/; https://manassaloi.com/booksummaries/2016/03/21/platform-revolution-parker-choudary.html; https://businessbookclub.substack.com/p/free-book-summary-platform-revolution

### L4 (fact, high) Opportunity Solution Tree: outcome → opportunities → solutions → assumption tests. The owner's ideas are solutions and the pains are opportunities
Torres's tree has four layers. The root is a desired outcome, and she recommends product outcomes ('customer behavior in the product or sentiment'), not business or traction metrics. Opportunities are 'an unmet customer need, pain point, or desire'. Test: 'Is there more than one way to address this opportunity?' If not, it is a solution disguised as an opportunity. She advises comparing at least three solutions per target opportunity, then testing assumptions.

Applied to the owner's list:
- Opportunities: 'I can't understand what happened when I asked for all customers'; 'the card I asked for failed on a date field'; 'traces are unreadable'; 'I need to trust the system with my password / DOB'.
- Solutions: ValueNeuron<string> as a DurableGrain; NuGet-like packages; Reqnroll feature discovery; per-neuron tariffs.

highlevel.md should hold the tree, so each solution epic traces back to an opportunity and an outcome, and undecided solutions list their alternatives.
Evidence: https://www.producttalk.org/opportunity-solution-trees/; https://www.producttalk.org/2023/12/opportunity-solution-trees/

### L5 (fact, high) JTBD job stories ('When … I want to … so I can …') suit a marketplace for non-programmers better than persona stories
Intercom invented job stories and Alan Klement named them: 'When ____, I want to ____, so I can ____'. The three parts are situation, motivation and outcome. Intercom's case against 'As a <persona>…' stories: personas 'lack causality', stories merge implementation with assumed motivation, and they 'disregard context, situations, and anxieties'.

For a creator/consumer marketplace, anxieties matter: cost, safety of secrets, what an app will do. Example: 'When I have a folder of product photos and the assistant proposes BackgroundRemover, I want to see the Compute estimate and approve it, so I can list the products today without surprise charges.'

Recommendation: use job stories on features, and keep Connextra 'As a…' stories optional for work items.
Evidence: https://www.intercom.com/blog/using-job-stories-design-features-ui-ux/; https://medium.com/the-job-to-be-done/replacing-the-user-story-with-the-job-story-af7cdee10c27; https://www.intercom.com/blog/accidentally-invented-job-stories/

### L6 (fact, medium) Business capability map: a stable 'what, not how' view, 10-15 level-1 domains, level-2 breakdown, a heat map for gaps
The Open Group defines a capability as 'an ability to do something'. A capability map is 'the complete, stable set of business capabilities'. It is 'independent of the current organizational structure, business processes, information systems and applications'. Level 1 is typically 10-15 domains; level 2 breaks each domain down. The primary TOGAF page is behind a login; these quotes come from search excerpts of the Open Group guide and secondary summaries.

A capability map is the right spine for highlevel.md, a document that is meant to list capabilities. Give each capability:
- a maturity score (Missing / Partial / Usable / Solid)
- a repo evidence path
- the epic IDs

This directly shows the owner's 'a lot is missing' as a heat map, not as a list of opinions. The repo modules (src/Modules: AI, ClickHouse, Coding, DigitalBrain, Google, Memory, Microsoft, Salesforce, Supabase, Time) are implementations. They map onto capabilities; they are not the capabilities themselves.
Evidence: https://pubs.opengroup.org/togaf-standard/business-architecture/business-capabilities.html; https://governance.foundation/assets/frameworks/togaf/g189%20-%20Business%20Capbility.pdf; https://en.wikipedia.org/wiki/Business_capability_model; src/Modules (directory listing: AI, ClickHouse, Coding, DigitalBrain, Google, Memory, Microsoft, Salesforce, Supabase, Time)

### L7 (fact, medium) SAFe epics have a hypothesis statement, business outcomes, leading indicators, NFRs, MVP and a lean business case; business vs enabler
SAFe defines an epic as 'a significant initiative that requires portfolio-level oversight'. There are two types: business epics (customer value) and enabler epics (architectural runway).

The epic hypothesis template: 'For <customers> who <need>, the <solution> is a <type> that <benefit>. Unlike <current state/competitor>, our solution <differentiator>.' It is followed by:
- Business Outcomes: measurable
- Leading Indicators: 'early measurements, often experiments, to determine whether the hypothesis is true'
- NFRs

Atlassian's SAFe lean business case template adds: in/out-of-scope, NFRs, 'the MVP that will be used to test the hypothesis', cost estimate, value return, and a go/no-go recommendation.

On sourcing: framework.scaledagile.com shows only definitions without a login. The template text comes from Atlassian's template and practitioner write-ups, so the exact SAFe wording is not verified.

All of the owner's big items are testable bets and fit this template: 'Compute currency', 'SDK type system', 'Marketplace', 'Vector discovery of the system'. The type system and durable neurons are enabler epics. The marketplace and Compute are business epics.
Evidence: https://framework.scaledagile.com/epic; https://www.atlassian.com/software/confluence/templates/safe-lean-business-case; https://www.agilerising.com/blog/safe-epic-real-world-example/; https://agileseekers.com/blog/lean-business-case-epic-hypothesis-and-mvp-evidence-in-safe

### L8 (fact, medium) SAFe features carry a benefit hypothesis plus acceptance criteria and fit in one PI; Azure DevOps Value Area mirrors business vs enabler
SAFe: 'A Feature represents solution functionality that delivers business value, fulfills a stakeholder need, and is sized to be delivered by an Agile Release Train within a PI.' Features split into stories. Enabler features carry NFR work.

A common benefit-hypothesis form: 'We believe this [business outcome] will be achieved if [these users] successfully achieve [this user outcome] with [this feature]'. Each hypothesis has one or more acceptance criteria.

Azure DevOps has a matching 'Value Area' field on Epic, Feature and Story with two values:
- Architectural: 'technical services to implement business features'
- Business: 'directly deliver customer value'

Microsoft's SAFe guide says: 'Set Value Area = Architectural for Features mapped to architecture epics.'

Recommendation: every feature file gets a benefit hypothesis, acceptance criteria and `value-area: business|enabler`.
Evidence: https://framework.scaledagile.com/features-and-capabilities; https://airfocus.com/glossary/minimum-requirements-for-feature/; https://learn.microsoft.com/azure/devops/boards/backlogs/define-features-epics?view=azure-devops#add-details-to-a-feature-or-epic; https://learn.microsoft.com/azure/devops/boards/plans/safe-configure-boards?view=azure-devops

### L9 (fact, high) Azure DevOps hierarchy: Epic → Feature → Story/PBI → Task, linked only between types, with standard planning fields
Microsoft Learn: 'Epics group Features, Features group Requirements (User Stories, Product Backlog Items…), and Requirements group Tasks. You connect items across the hierarchy with parent-child links.' It also says: 'link parents and children only across different types… Avoid same-type hierarchies like story-to-story'. If you nest same-type items, only the leaf items appear on boards.

Sizing guidance: Epic = 'large initiatives that span multiple features or releases'. Feature = 'customer-facing value that ships as a coherent capability'. Requirement = 'scoped to a single iteration'.

Standard fields:
- Epic/Feature: Business Value, Time Criticality, Effort, Risk, Target Date
- Story: Acceptance Criteria ('criteria to be met before the… user story can be closed'), Story Points / Effort

Recommendation: keep exactly three markdown levels (epic / feature / work item), with tasks as a checklist inside the work item. Never nest a work item under a work item. Name frontmatter fields after these ADO fields so a later import to Azure Boards or Jira is mechanical.
Evidence: https://learn.microsoft.com/azure/devops/boards/work-items/about-work-items?view=azure-devops#track-work-with-different-work-item-types; https://learn.microsoft.com/azure/devops/boards/backlogs/backlogs-overview?view=azure-devops#display-leaf-node-work-items; https://learn.microsoft.com/azure/devops/boards/best-practices-agile-project-management?view=azure-devops#choose-work-item-types; https://learn.microsoft.com/azure/devops/boards/queries/planning-ranking-priorities?view=azure-devops#fields-used-to-plan-and-prioritize-work; https://learn.microsoft.com/azure/devops/boards/work-items/guidance/agile-process-workflow?view=azure-devops#define-user-stories

### L10 (fact, high) Sizing heuristics: 2-3 epics per quarter, stories fit a 1-2 week sprint. Shape Up adds appetite and no-gos for small teams
Atlassian: stories are 'something the team can commit to finish within a one- or two-week sprint'. 'Epics… are few in number and take longer to complete. Teams often have two or three epics they work to complete each quarter.' Initiatives are collections of epics.

Basecamp Shape Up describes a pitch with five parts:
- Problem
- Appetite: 'How much time we want to spend and how that constrains the solution'
- Solution
- Rabbit holes
- No-gos: 'Anything specifically excluded…'

IntoChat is effectively one owner plus AI agents, so a fixed time budget ('appetite') and explicit no-gos on each epic are cheap guards against scope creep. The owner's 'a lot of trash' comment suggests scope creep is a real risk here. Recommendation: add `appetite:` and a 'Non-goals' section to the epic and feature templates, and keep only 2-3 epics in 'Now'.
Evidence: https://www.atlassian.com/agile/project-management/epics-stories-themes; https://www.atlassian.com/agile/project-management/epics; https://basecamp.com/shapeup/1.5-chapter-06

### L11 (fact, high) Work-item quality: INVEST, SMART tasks, 3Cs. DoD is a Scrum commitment; DoR is optional and can turn into a stage gate
INVEST (Bill Wake):
- Small: 'at most a few person-weeks'
- Testable: 'I understand what I want well enough that I could write a test for it'
- Negotiable: 'not an explicit contract for features'

SMART tasks: Specific, Measurable ('can we mark it as done?'), Achievable, Relevant, Time-boxed.

Ron Jeffries' 3Cs: the Card has 'just enough text to identify the requirement'; Conversation; and Confirmation, which 'is the acceptance test'.

Scrum Guide: 'The Definition of Done is a formal description of the state of the Increment when it meets the quality measures required.' Items that can be Done within one Sprint are 'deemed ready for selection'. Scrum.org: DoR 'is not mentioned in the Scrum Guide' and 'can be weaponized'.

Recommendation: epics/README.md holds one DoD. That DoD should match the owner's standing rules: Aspire build and run, integration tests green, code review done. It also holds a short DoR checklist (parent linked, acceptance criteria present, no [NEEDS CLARIFICATION] left, INVEST-small). Treat the DoR as a checklist, not a gate.
Evidence: https://xp123.com/invest-in-good-stories-and-smart-tasks/; https://ronjeffries.com/xprog/articles/expcardconversationconfirmation/; https://scrumguides.org/scrum-guide.html; https://www.scrum.org/resources/blog/why-isnt-definition-ready-described-scrum-guide; https://www.scrum.org/resources/blog/ready-or-not-demystifying-definition-ready-scrum

### L12 (fact, high) Acceptance criteria formats: Gherkin for behaviour (3-5 steps, no implementation), EARS for system/NFR requirements
Gherkin keywords:
- Feature: 'group related scenarios'
- Rule: 'one business rule'
- Scenario/Example: 'a concrete example that illustrates a business rule'
- Given: initial context
- When: event
- Then: expected outcome
- Background and Scenario Outline / Examples

Cucumber recommends '3-5 steps per example' and says 'implementation details should be hidden in the step definitions'.

EARS (Mavin, Rolls-Royce, RE'09) uses the form 'WHILE <precondition> WHEN <trigger> the <system> SHALL <response>'. Kiro uses it in requirements.md.

Recommendation: write feature and work-item acceptance criteria in Gherkin, in the customer's language. Examples: 'Given a personal-details card with a date-of-birth field… Then the behavior is Running'; 'Given the assistant proposes BackgroundRemover costing 12 Compute… When I approve…'. Write epic NFRs (secret handling, metering accuracy, trace volume) in EARS, e.g. 'WHEN a neuron persists a value of kind Secret THE SYSTEM SHALL NOT emit the plaintext in any signal or trace.'
Evidence: https://cucumber.io/docs/gherkin/reference/; https://alistairmavin.com/ears/; https://kiro.dev/docs/specs/feature-specs/

### L13 (risk, high) Executable BDD files keep appearing and disappearing in this repo, so tie acceptance criteria to markdown first, not to Reqnroll
The owner suggests describing apps with Reqnroll feature files that can be found by vector search. Repo history shows executable Gherkin has come and gone:
- 977c093e8 (2026-08-24) 'delete dead BDD lane'
- 6fed0209e (2026-08-25) 'compile Reqnroll behavior features'
- 2950bd71b (2026-09-07) 'Keep only Reqnroll BDD tests', which is an ancestor of master
- c6cba6470 (2026-09-17) deleted 26 *.feature files, including tests/DigitalBrain.Tests/Features/surface.feature and src/Modules/Time/Tests/Scenarios/time.feature

`git ls-files '*.feature'` on master now returns nothing, and Directory.Packages.props has no Reqnroll entry.

Consequence: if acceptance criteria live only in executable .feature files, they are lost whenever the test stack changes. Recommendation: keep Gherkin as plain text inside the feature and work-item markdown, which is stable and vector-indexable. Whether to make it executable (Reqnroll or otherwise) is a separate ADR. Work items then link to whatever tests verify them (`links.tests`).
Evidence: git show --stat c6cba6470 -- '*.feature' (26 files, 1292 deletions, 2026-09-17); git log 2950bd71b 'Keep only Reqnroll BDD tests.' (2026-09-07, ancestor of master); git log 977c093e8 'refactor(testing): delete dead BDD lane…' (2026-08-24); git log 6fed0209e 'feat: compile Reqnroll behavior features' (2026-08-25); Directory.Packages.props (no Reqnroll/SpecFlow entry)

### L14 (fact, medium) Prioritisation methods: WSJF for epics and features, MoSCoW within a release slice, Kano for marketplace must-bes; RICE needs usage data
RICE (Intercom) = (Reach × Impact × Confidence) / Effort.
- Impact: 3, 2, 1, 0.5 or 0.25
- Confidence: 100%, 80% or 50%
- Effort: person-months

WSJF (SAFe) = relative Cost of Delay ÷ job size. Cost of Delay = user-business value + time criticality + risk reduction / opportunity enablement. Azure DevOps already has Business Value, Time Criticality and Effort fields on Epic and Feature.

MoSCoW (DSDM): Must Haves should be 'no more than 60% effort', with about 20% Could Haves.

Kano: Must-be, One-dimensional, Attractive, Indifferent, Reverse.

My assessment (opinion): before launch, RICE's Reach is guesswork. WSJF's risk-reduction / opportunity-enablement term properly rewards enabler epics such as the type system or durable values, which unblock the marketplace. Kano flags the must-bes of a marketplace that handles money and secrets: approval before spend, transparent bills, safe secret storage. Recommendation: put the WSJF components in epic and feature frontmatter, MoSCoW on features inside a Now slice, and a Kano class on features.
Evidence: https://www.intercom.com/blog/rice-simple-prioritization-for-product-managers/; https://framework.scaledagile.com/wsjf; https://agilebusiness.org/dsdm-project-framework/moscow-prioritisation.html; https://en.wikipedia.org/wiki/Kano_model; https://learn.microsoft.com/azure/devops/boards/queries/query-numeric?view=azure-devops#fields-used-to-estimate-and-track-work

### L15 (fact, high) Now/Next/Later roadmaps rank problems by confidence, not dates, and tag each item with an objective
Janna Bastow (ProdPad, 2012): Now items are 'clearly defined, much more detailed', broken into granular work. Next items have 'fewer specifics'. Later items are 'big boulder blocks that you can see in the distance but don't need to break down yet'. 'You should prioritize problems, not ideas.' Each initiative should connect to a business objective, made 'visual and obvious' with labels.

This maps onto the hierarchy: Now = epics with features and work items; Next = epics with features only; Later = epic stubs (hypothesis only). Recommendation: highlevel.md's roadmap section is a Now/Next/Later table of epic IDs, each tagged with an outcome, and each epic's frontmatter carries `horizon: now|next|later`. No dates.
Evidence: https://www.prodpad.com/blog/invented-now-next-later-roadmap/; https://www.prodpad.com/glossary/now-next-later-roadmap/

### L16 (fact, medium) Story mapping: a backbone of user activities plus a walking skeleton (thinnest end-to-end slice) defines the MVP
Jeff Patton's user story map puts the backbone (high-level user activities) on the horizontal axis and release slices vertically. The walking skeleton is 'the thinnest possible horizontal slice across the entire map that still delivers a complete end-to-end user experience'; Patton borrowed the term from Cockburn.

The backbone for the platform + marketplace: discover an app → understand what it does and costs → approve → run → see the result and the bill → manage (update / uninstall). A walking-skeleton release: one creator publishes one app → the assistant finds it by vector search → the user approves a Compute charge → the app runs → the ledger shows the settled charge.

The owner's two painful flows ('Show me all customers'; 'draw a card with name, surname, date of birth') should become named journeys in highlevel.md. Their Gherkin acceptance examples serve as regression checks for understandability. Sources are secondary (Patton's book is not freely accessible).
Evidence: https://whichframework.org/frameworks/user-story-mapping.html; https://www.visual-paradigm.com/guide/the-complete-guide-to-user-story-mapping/

### L17 (fact, high) ADRs (Nygard / MADR): numbers never reused, superseded records kept, frontmatter for status — use them for the owner's open architecture questions
Nygard's ADR sections: Title, Context, Decision ('We will…'), Status (proposed / accepted), Consequences ('not just the positive ones'). 'ADRs will be numbered sequentially and monotonically. Numbers will not be reused.' 'If a decision is reversed, we will keep the old one around, but mark it as superseded.'

MADR adds YAML frontmatter (status: proposed|rejected|accepted|deprecated|superseded by ADR-NNNN, date, decision-makers, consulted, informed). Its sections include Decision Drivers, Considered Options, and Confirmation. Files are named `docs/decisions/NNNN-title-with-dashes.md`.

The owner's questions are decisions, not features, and each needs its own ADR that epics link to:
- 'Can every neuron be a durable grain?'
- 'ValueNeuron<T> vs typed state'
- 'NuGet vs manifest packaging'
- 'Compute unit and price source'

The repo has no ADR folder today; the only matches for 'decision' are C# contract files.
Evidence: https://www.cognitect.com/blog/2011/11/15/documenting-architecture-decisions; https://adr.github.io/madr/; git ls-files | grep -iE 'adr|decision' → only src/Modules/Memory/Contracts/Memory/ProtectedPayloadReference.cs, src/Modules/Microsoft/GitHub/Contracts/GitHub/ReadReviewEvidence.cs

### L18 (fact, high) RFC and KEP conventions provide a proven status enum, goals/non-goals, and replace / see-also links for git-hosted proposals
Rust RFC template: metadata (Feature Name, Start Date, RFC PR, Issue) plus Summary, Motivation, Guide-level explanation ('as if it was already included'), Reference-level explanation, Drawbacks, Rationale and alternatives, Prior art, Unresolved questions, Future possibilities.

Kubernetes KEP metadata (kep.yaml): title, kep-number, authors, owning-sig, participating-sigs, status, creation-date, reviewers, approvers, see-also, replaces, stage, milestone.
- status values: `provisional|implementable|implemented|deferred|rejected|withdrawn|replaced`
- stage values: `alpha|beta|stable`

KEP README sections: Summary, Motivation (Goals / Non-Goals), Proposal (User Stories, Risks and Mitigations), Design Details (Test Plan, Graduation Criteria), Implementation History, Drawbacks, Alternatives.

Recommendation: borrow the KEP status values (with 'provisional' renamed 'proposed'), `replaces` / `see-also` links, Goals / Non-Goals, and a 'Guide-level explanation'. The guide-level explanation directly addresses the owner's complaint that the product is hard to understand: each feature explains itself to a user as if it had already shipped.
Evidence: https://github.com/rust-lang/rfcs/blob/master/0000-template.md; https://github.com/kubernetes/enhancements/blob/master/keps/NNNN-kep-template/kep.yaml; https://github.com/kubernetes/enhancements/blob/master/keps/NNNN-kep-template/README.md

### L19 (pattern, high) Planning conventions built for AI agents: one file per item, ID-prefixed names, frontmatter, and explicit [NEEDS CLARIFICATION] markers
GitHub Spec Kit: 'Constitution once per project; specify → plan → tasks → implement'. Each feature gets `specs/<NNN-feature>/`, containing spec.md, plan.md, research.md, data-model.md, contracts/, quickstart.md and tasks.md. Feature numbers are assigned automatically (001, 002, …). Authors must 'Mark all ambiguities: Use [NEEDS CLARIFICATION: specific question]', and a spec cannot proceed until none remain.

Backlog.md stores tasks as markdown in `backlog/`, named `task-<id> - <title>.md` with IDs like TASK-1 and metadata (status, assignee, labels, dependencies, parent, priority). It is aimed at 'AI agents write the code. You review the tasks.'

Kiro uses requirements.md (EARS), design.md and tasks.md.

Write the Docs defines docs-as-code as using the same tools as code: issue trackers, git, plain-text markup, code review and automated tests.

The owner works through AI subagents, as the repo's superpowers plans show. Machine-parsable frontmatter plus a clarification marker lets agents find open questions and parent links without guessing. The same files can be embedded into the Memory module's Qdrant index, so the assistant can 'find any part of the system'.
Evidence: https://github.com/github/spec-kit; https://github.com/github/spec-kit/blob/main/spec-driven.md; https://github.com/MrLesk/Backlog.md; https://kiro.dev/docs/specs/feature-specs/; https://www.writethedocs.org/guide/docs-as-code/

### L20 (gap, high) Repo planning docs have no IDs, frontmatter or hierarchy; status is prose, and plan checkboxes are not a reliable status
There are 40 markdown files under docs/. Only 7 have a 'Status:' line, and each is free-form prose, e.g. 'approved by the user on 2026-09-19. Foundation and Time pilot implemented…' (docs/superpowers/specs/2026-09-19-framework-simplification-design.md:4). No doc uses an EPIC-/FEAT-/TASK- style ID (a grep for such IDs found no matches).

Plan checkboxes are not kept current:
- docs/superpowers/plans/2026-09-19-framework-foundation-and-time.md has 56 unchecked and 0 checked boxes, although the spec above says the pilot was implemented.
- docs/superpowers/plans/2026-09-22-programmable-behaviors.md has 45 unchecked and 0 checked, although its line 3 links an 'Implementation and actual verification: stage-2 delivery record'.

Consequence: nobody, human or agent, can answer 'what is done, what is next, why' from the repo. Recommendation: status lives in exactly one frontmatter field per epic, feature and work item. Specs and plans stay as they are: execution artifacts linked from work items (`links.specs`, `links.plans`), not the source of status.
Evidence: docs/superpowers/specs/2026-09-19-framework-simplification-design.md:4; docs/superpowers/plans/2026-09-22-programmable-behaviors.md:3; docs/superpowers/plans/2026-09-19-framework-foundation-and-time.md (grep -c '- [ ]' = 56, '- [x]' = 0); docs/superpowers/plans/2026-09-22-programmable-behaviors.md (grep -c '- [ ]' = 45, '- [x]' = 0); find docs -name '*.md' | wc -l = 40; grep -l '^Status:' -r docs | wc -l = 7

### L21 (fact, high) Existing repo artifacts that highlevel.md should link to, not duplicate
1. CONTEXT.md is a domain glossary (Neuron, Signal, Synapse, Journal, Entity, IDigitalBrain, Script, Behavior) with '_Avoid_' terms. It is the right glossary for highlevel.md. New product terms (App, Marketplace, Listing, Compute, Tariff, Approval, ValueNeuron) should be added there as 'proposed'.

2. docs/intochat-customer-product-design.md (Status: proposed, 21 Sept 2026) already has sections 'Release decision' (line 7), 'Custom apps' (line 94), 'Compute and trust' (line 106) and 'Release acceptance examples' (line 131). It also sets a first release of five core apps (Image Studio, Files, Tables, Notes, Automations), with 'a broad catalog' as 'a later expansion' (line 9). That may conflict with the owner's new marketplace-first framing, and needs an explicit decision.

3. docs/superpowers/specs|plans use date-prefixed names and prose headers ('Status', 'Outcome', 'Evidence', 'Architecture decision'); plans link to their spec.

4. docs/intochat-neuron-app-composition.md covers app definitions and instances.

Recommendation: highlevel.md becomes the index over these. Each capability row and epic cites the existing doc by path.
Evidence: CONTEXT.md:1-5; docs/intochat-customer-product-design.md:3; docs/intochat-customer-product-design.md:9; docs/intochat-customer-product-design.md:94; docs/intochat-customer-product-design.md:106; docs/intochat-customer-product-design.md:131; docs/intochat-neuron-app-composition.md:140; docs/superpowers/specs/2026-09-22-programmable-behaviors-design.md:1-7

### L22 (recommendation, medium) Recommended highlevel.md outline for a platform + marketplace + AI assistant
Proposed location: repo root (sibling of CONTEXT.md). Frontmatter: `id: HL`, `status: draft|ratified`, `owner`, `updated`, `version`.

0. How to read this doc: links to CONTEXT.md, epics/README.md and docs/decisions/.
1. Press release, one page at most (L1): headline, subheading, summary, problem, solution, customer quote, getting started.
2. Vision board (L2): vision (2-5 yrs); target groups: end users, non-programmer creators, developers/neuron authors, workspace admins; needs; 3-5 standout capabilities; business goals; extended board: revenue (Compute), costs (LLM tokens, storage), channels, competitors.
3. Platform model (L3): core interaction (participants → value unit = app listing with capabilities + permissions + Compute tariff → filter = assistant vector search); pull, facilitate, match; governance and trust rules (approval before spend, no implicit scope grants, secrets never in signals or traces).
4. Product principles / tenets and global non-goals (e.g. 'English is how the owner asks; a compiled script is what they get' from CONTEXT.md).
5. Glossary: pointer to CONTEXT.md plus new proposed terms.
6. Outcomes and metrics: one north-star product outcome plus 3-5 product outcomes with leading indicators (L4, L7).
7. Opportunity map (L4): opportunities from the owner's pains, each with evidence, candidate solutions (≥3 where undecided) and epic IDs.
8. Capability map with heat map (L6). Level-1 rows, each with maturity, evidence path and epics:
   - Neuron runtime & durability
   - SDK type system & typed values (incl. secrets/PII: DOB, password)
   - Discovery & system memory (vector index of neurons, modules, apps, specs)
   - Assistant & agent orchestration
   - Behaviors & automation
   - Workspace UI & surfaces (field kinds, e.g. date)
   - App packaging, manifest & versioning
   - Marketplace: publish, review, list, install, update, uninstall
   - Compute: metering, tariffs, reservation, ledger, approvals
   - Identity, permissions, consent & secrets
   - Connectors (Supabase, Google, Microsoft, Salesforce, ClickHouse)
   - Observability & operability (trace hygiene)
   - Creator & developer experience (SDK, templates, docs, test harness)
9. Key journeys (L16): backbone plus walking skeleton. Named journeys with Gherkin examples: 'Show me all customers', 'Draw a personal-details card', 'Install LeadGenerator from the marketplace', 'Assistant uses BackgroundRemover after approval'.
10. Roadmap: Now / Next / Later of epic IDs with outcome tags (L15).
11. Prioritisation: method and WSJF table (L14).
12. Assumptions and risks, including 'top three reasons this will not succeed'.
13. Open decisions: table of ADR IDs with status (L17).
14. FAQ: external and internal.
15. Change log.

Keep sections 1-3 persuasive and short. Put the detail in sections 7-9, which should mostly link to epics.
Evidence: https://workingbackwards.com/resources/working-backwards-pr-faq/; https://www.romanpichler.com/blog/the-product-vision-board/; https://www.producttalk.org/opportunity-solution-trees/; https://www.prodpad.com/blog/invented-now-next-later-roadmap/; CONTEXT.md:1-5; src/Modules (AI, ClickHouse, Coding, DigitalBrain, Google, Memory, Microsoft, Salesforce, Supabase, Time)

### L23 (recommendation, medium) Recommended epics/ layout: global IDs that are never reused, folders nested by parent, and a frontmatter schema
Layout:
```
epics/
  README.md                 # conventions, status enums, ID counters, DoR/DoD, index table
  _templates/epic.md feature.md work-item.md
  EP-001-sdk-type-system/
    README.md               # the epic (renders on folder view)
    FT-001-typed-value-neurons/
      README.md             # the feature
      WI-001-value-kinds-date-secret.md
      WI-002-secret-storage-path.md
docs/decisions/0001-durable-vs-plain-neurons.md   # MADR (L17)
```

IDs:
- Each type has its own counter: EP-NNN, FT-NNN, WI-NNNN.
- Numbers only ever increase and are never reused, as with Nygard's ADR rule.
- Use global IDs, not hierarchical ones like E1.F2.W3, so moving an item to a new parent (git mv) keeps its ID.
- File and folder names are `<ID>-<kebab-title>`.
- Commits and PRs cite IDs, e.g. 'WI-0042: …'.

Shared frontmatter:
```yaml
id: FT-017
type: feature            # epic | feature | work-item
title: Model token tariffs
status: proposed         # proposed|analyzing|ready|in-progress|done|deferred|rejected|replaced
parent: EP-003           # none for epics
value-area: business     # business | enabler (ADO Value Area)
horizon: next            # now | next | later (epics; features inherit)
capability: [compute]    # keys from highlevel.md capability map
owner: vhorb
created: 2026-09-23
updated: 2026-09-23
links:
  depends-on: []
  replaces: null
  see-also: []
  adrs: []
  specs: []   # docs/superpowers/specs/...
  plans: []
  code: []    # src/... paths
  tests: []
  prs: []
```

Per-type extras:
- Epic: `wsjf: {business-value, time-criticality, risk-opportunity, size}`, `appetite`.
- Feature: `moscow`, `kano`.
- Work item: `kind: story|enabler|spike|bug|chore`, `estimate: S|M|L`.

A small lint check in CI would keep the links honest (docs-as-code, L19):
- the parent exists
- status is a valid enum value
- a done work item has at least one `tests` or `prs` link
- no `[NEEDS CLARIFICATION` remains in anything that is ready
Evidence: https://learn.microsoft.com/azure/devops/boards/backlogs/backlogs-overview?view=azure-devops#display-leaf-node-work-items; https://github.com/kubernetes/enhancements/blob/master/keps/NNNN-kep-template/kep.yaml; https://adr.github.io/madr/; https://www.cognitect.com/blog/2011/11/15/documenting-architecture-decisions; https://github.com/MrLesk/Backlog.md; https://github.com/github/spec-kit/blob/main/spec-driven.md

### L24 (recommendation, medium) Short templates for epics, features and work items
EPIC (epics/EP-NNN-slug/README.md), after the frontmatter:
```
# EP-NNN <Title>
## Hypothesis
For <target group> who <need>, <epic name> is a <type> that <benefit>. Unlike <today/competitor>, it <differentiator>.
## Opportunity & outcome   (link to highlevel.md §7; product outcome it moves)
## Business outcomes (measurable)   ## Leading indicators
## Scope — in   ## Non-goals   ## Appetite
## MVP (smallest test of the hypothesis)
## NFRs (EARS: WHEN … THE SYSTEM SHALL …)
## Features   | ID | Title | Status | MoSCoW |
## Assumptions & how we test them   ## Risks
## Open questions   [NEEDS CLARIFICATION: …]   ## Decisions → ADR-NNNN
```

FEATURE (EP-…/FT-NNN-slug/README.md):
```
# FT-NNN <Title>
## Job story   When <situation>, I want to <motivation>, so I can <outcome>.
## Benefit hypothesis   We believe <business outcome> will be achieved if <users> achieve <user outcome> with <this feature>.
## Guide-level explanation (as if shipped; 1 paragraph, customer language)
## Goals   ## Non-goals
## Acceptance criteria (Gherkin, 3–5 steps each)
Scenario: <name>
  Given … When … Then …
## Work items   | ID | Title | Kind | Status |
## Feature DoD additions (e.g., trace volume budget, Compute metered)
```

WORK ITEM (…/WI-NNNN-slug.md):
```
# WI-NNNN <Title>
<One line: As a <role> I want <goal> so that <benefit> — or the task intent for enablers/spikes>
## Acceptance criteria   Given/When/Then (or checklist for chores)
## Tasks (SMART)   - [ ] …
## Notes   [NEEDS CLARIFICATION: …]
## Done evidence   tests: …  trace/run id: …  PR: …
```

epics/README.md holds:
- the DoR checklist: parent set, acceptance criteria written, INVEST-small, no clarification markers
- a single DoD: builds, Aspire run OK, integration tests green, code review done, docs/CONTEXT.md updated, frontmatter status set to done with evidence links
Evidence: https://www.atlassian.com/software/confluence/templates/safe-lean-business-case; https://www.agilerising.com/blog/safe-epic-real-world-example/; https://airfocus.com/glossary/minimum-requirements-for-feature/; https://www.intercom.com/blog/using-job-stories-design-features-ui-ux/; https://github.com/rust-lang/rfcs/blob/master/0000-template.md; https://cucumber.io/docs/gherkin/reference/; https://xp123.com/invest-in-good-stories-and-smart-tasks/; https://scrumguides.org/scrum-guide.html

### L25 (recommendation, medium) Order of work: put the owner's pains and ideas into the tree before writing epics
Suggested order:
1. Write highlevel.md sections 1-3 and 6-8 first: press release, vision, platform model, outcomes, opportunity map, capability heat map.
2. Classify every owner input as an opportunity, a candidate solution or a decision (ADR).
   - Opportunities: 'hard to understand', 'date field kind rejected → behavior Failed', 'too much trash in traces', 'need trust for DOB / password'.
   - Candidate solutions: ValueNeuron<string>, NuGet-like app packages, Reqnroll-described apps, per-neuron tariffs, a vector index of neurons.
   - Decisions (ADRs): every neuron a durable grain?; Compute unit and price source; package format.
3. Create enabler epics for foundations (type system and typed values, durability, discovery index, trace hygiene) and business epics for Marketplace, Compute and Apps. Score them with WSJF and place them in Now / Next / Later.
4. Write features and work items only for Now epics. Next epics get features only; Later epics get hypothesis stubs only (L15).

This follows Torres's warning against solutions disguised as opportunities and Pichler's advice to keep the board concise and leave detail to the backlog.
Evidence: https://www.producttalk.org/2023/12/opportunity-solution-trees/; https://www.romanpichler.com/blog/the-product-vision-board/; https://www.prodpad.com/blog/invented-now-next-later-roadmap/; docs/intochat-customer-product-design.md:106


## Open questions
- Is the first release marketplace-first (owner's new framing) or the five core apps (Image Studio, Files, Tables, Notes, Automations) set in docs/intochat-customer-product-design.md:9, with the marketplace as 'a later expansion'?
- Which target group is primary for v1: non-programmer app creators, developers/neuron authors, or end users consuming apps? This drives the press release and the walking skeleton.
- Hierarchy depth: three levels (Epic → Feature → Work item), or add an Initiative/Theme level above epics (e.g. 'Platform', 'Marketplace', 'Compute')?
- IDs: global counters that are never reused (EP-/FT-/WI-, survive re-parenting), or hierarchical IDs (E1.F2.W3, readable but break when items move)?
- Where should highlevel.md and epics/ live: repo root next to CONTEXT.md, or under docs/? Should docs/superpowers/specs|plans stay as execution artifacts linked from work items?
- Prioritisation method: WSJF scores in frontmatter, or just the order within Now/Next/Later? Is MoSCoW or Kano classification wanted on features?
- Should Gherkin acceptance criteria remain documentation-only in markdown, or become executable again (Reqnroll or other), given .feature files were deleted on 2026-09-17?
- Should the planning markdown (epics, features, capability map) be indexed into the Memory/Qdrant module, so the assistant can find 'any part of the system' including the product plan itself?
- Is Azure Boards, Jira or GitHub Issues a likely future home, so that frontmatter field names should match ADO fields exactly (Business Value, Time Criticality, Effort, Value Area, Acceptance Criteria)?
- What goes into the single Definition of Done: e.g. Aspire run, integration tests green, code review, a trace-noise budget, and Compute metering verified for paid paths?