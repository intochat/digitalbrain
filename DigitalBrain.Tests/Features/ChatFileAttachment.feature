@cluster
Feature: Chat file attachments visualized with UI kit tables

  As a user chatting with the system
  I want to drag and drop an Excel (or tabular) file into chat
  So that it is visualized immediately as a clean UI kit table
  And the data is available to the brain for further questions or tasks

  Scenario: Dropping a simple sales Excel renders a uikit table surface
    Given a chat session "sales-chat"
    When the user drops a file named "q2-sales.xlsx" with the following tabular data:
      | Month | Revenue | Units |
      | Jan   | 12000   | 45    |
      | Feb   | 14500   | 52    |
      | Mar   | 13800   | 48    |
    Then the timeline contains a TableSurface for the chat
    And the table surface has columns "Month", "Revenue", "Units"
    And the table surface has 3 data rows
    And the first row starts with "Jan"

  Scenario: Table from dropped file is usable by the assistant
    Given a chat session "sales-chat"
    And the user previously dropped "q2-sales.xlsx" with 3 months of data
    When I ask "what was the best month by revenue"
    Then the assistant response references the table data from the attachment
    And no error surfaces are emitted
