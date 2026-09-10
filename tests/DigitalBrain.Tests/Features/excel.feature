Feature: Excel
  Spreadsheet edits are normalised and saved through scheduled reactions.

  Scenario: A replacement grid is normalised on apply
    Given a running brain
    When "claude" applies a replacement to sheet "replacement" with blank title and sheet name and ragged rows
      | Item | Quantity | Note  |
      | Tea  |          |       |
      | Cake | 2        | Fresh |
    Then the spreadsheet command is accepted
    And "claude" waits up to 5 seconds until sheet "replacement" has title "Sheet" and sheet name "Sheet1" with the grid
      | Item | Quantity | Note  |
      | Tea  |          |       |
      | Cake | 2        | Fresh |

  Scenario: A cell edit grows the sheet within its caps
    Given a running brain
    When "claude" applies a cell edit to sheet "growing" at row 3 and column 27 with value "Tea"
    Then the spreadsheet command is accepted
    And "claude" waits up to 5 seconds until sheet "growing" has 4 rows and 28 columns with value "Tea" at row 3 and column 27
    And "claude" waits up to 5 seconds until sheet "growing" has column headers "A,B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,Z,AA,AB"
    And "claude" reads 1 row and 1 column from sheet "growing" at row 3 and column 27 with header "AB" and value "Tea"

  Scenario: A cell outside the caps is refused
    Given a running brain
    When "claude" tries to apply a cell edit to sheet "capped" at row 64 and column 0 with value "Tea"
    Then the spreadsheet command fails with "64 rows"
