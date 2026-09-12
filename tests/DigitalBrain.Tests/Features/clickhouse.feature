Feature: ClickHouse
  A read-only ClickHouse neuron and live query tables, served here by the in-memory fake provider.

  Scenario: The schema of the configured database is readable
    Given a running brain with the ClickHouse module
    When "claude" reads the ClickHouse schema
    Then the ClickHouse schema lists table "companies_current" with column "employee_count" of type "number"
    And the ClickHouse schema column "country" of "companies_current" offers the sample values "CZ, DE, GB"

  Scenario: A read-only query returns typed rows
    Given a running brain with the ClickHouse module
    When "claude" runs the ClickHouse query "SELECT name, employee_count FROM companies_current WHERE country = 'GB' LIMIT 10"
    Then the ClickHouse query returns 6 rows and is not truncated
    And the ClickHouse query column "employee_count" has type "number"

  Scenario: A write is refused before it reaches the server
    Given a running brain with the ClickHouse module
    When "claude" runs the ClickHouse query "INSERT INTO companies_current (company_id) VALUES ('x')"
    Then the ClickHouse query was refused with a message containing "read-only"

  Scenario: A query table pages and filters on the server
    Given a running brain with the ClickHouse module
    When "claude" creates query table "leads" titled "All companies" from "SELECT * FROM companies_current"
    Then "claude" waits up to 5 seconds until query table "leads" page 0 of 5 returns 5 rows of 12
    And query table "leads" page 5 of 5 returns 5 rows
    And query table "leads" page 10 of 5 returns 2 rows
    When "claude" filters query table "leads" where "employee_count" "gte" 50
    Then query table "leads" has 5 filtered rows of 12 at revision 2
    And query table "leads" page 0 of 5 returns 5 rows
    And a stale update of query table "leads" at revision 1 is a conflict

  Scenario: A query table announces a card
    Given a running brain with the ClickHouse module
    And "clickhouse-table:cards" is connected to "claude" for "TableRendered"
    When "claude" creates query table "cards" titled "UK leads" from "SELECT name FROM companies_current WHERE country = 'GB'"
    And "claude" waits up to 10 seconds for an incoming "TableRendered"
    Then "claude" incoming journal contains "TableRendered" {"name":"cards","title":"UK leads"}
