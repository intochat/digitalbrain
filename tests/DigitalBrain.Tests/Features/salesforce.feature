Feature: Salesforce writes and connection lifetime

  Background:
    Given a running brain with the Salesforce module in fake mode

  Scenario: A native write requires the reviewed schema and cannot be submitted twice
    When the Salesforce account connects
    Then the Salesforce connection is reported
    When a native Salesforce record creation is prepared
    Then the exact Salesforce preview is published without writing
    And Salesforce refuses a confirmation with another schema
    When the reviewed Salesforce write is confirmed twice
    Then Salesforce reports one written record
    And Salesforce refuses another confirmation of the consumed preview

  Scenario: Expired Salesforce credentials refuse reads without changing journals
    When a short lived Salesforce connection expires
    Then Salesforce user and query reads require reconnection without changing journals
    When Salesforce disconnects
    Then Salesforce reports no connection
