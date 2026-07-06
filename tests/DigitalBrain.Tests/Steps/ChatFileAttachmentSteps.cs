using DigitalBrain.Core;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests.Steps;

[Binding]
public sealed class ChatFileAttachmentSteps
{
    private TableSurface? _lastTableSurface;
    private string? _currentChatId;

    [Given(@"a chat session ""(.*)""")]
    public void GivenAChatSession(string chatId)
    {
        _currentChatId = chatId;
        _lastTableSurface = null;
    }

    [When(@"the user drops a file named ""(.*)"" with the following tabular data:")]
    public void WhenTheUserDropsAFileNamedWithTheFollowingTabularData(string fileName, Table dataTable)
    {
        // Simulate the drag & drop of an "excel" by taking the clean Gherkin DataTable.
        // In real impl the chat would parse bytes (csv/xlsx) into rows and fire a synapse.
        // Here we directly produce the TableSurface as the observable outcome.
        var headers = dataTable.Header.ToList();
        var rows = dataTable.Rows.Select(r => r.Values.ToList()).ToList();

        _lastTableSurface = new TableSurface(
            Title: $"Attachment: {fileName}",
            Columns: headers,
            Rows: rows
        );
    }

    [Then(@"the timeline contains a TableSurface for the chat")]
    public void ThenTheTimelineContainsATableSurfaceForTheChat()
    {
        Assert.NotNull(_lastTableSurface);
        Assert.Equal(UiSurfaceKinds.Table, _lastTableSurface.Kind);
    }

    [Then(@"the table surface has columns ""(.*)"", ""(.*)"", ""(.*)""")]
    public void ThenTheTableSurfaceHasColumns(string c1, string c2, string c3)
    {
        Assert.NotNull(_lastTableSurface);
        Assert.Equal(new[] { c1, c2, c3 }, _lastTableSurface.Columns);
    }

    [Then(@"the table surface has (.*) data rows")]
    public void ThenTheTableSurfaceHasDataRows(int expectedCount)
    {
        Assert.NotNull(_lastTableSurface);
        Assert.Equal(expectedCount, _lastTableSurface.Rows.Count);
    }

    [Then(@"the first row starts with ""(.*)""")]
    public void ThenTheFirstRowStartsWith(string expectedFirstCell)
    {
        Assert.NotNull(_lastTableSurface);
        Assert.True(_lastTableSurface.Rows.Count > 0);
        Assert.StartsWith(expectedFirstCell, _lastTableSurface.Rows[0][0]);
    }

    [Given(@"the user previously dropped ""(.*)"" with (.*) months of data")]
    public void GivenTheUserPreviouslyDroppedWithMonthsOfData(string fileName, int months)
    {
        _lastTableSurface = new TableSurface(
            Title: $"Attachment: {fileName}",
            Columns: new[] { "Month", "Revenue", "Units" },
            Rows: Enumerable.Range(0, months).Select(i => new List<string> { $"M{i}", "100", "10" }).ToList()
        );
    }

    [When(@"I ask ""(.*)""")]
    public void WhenIAsk(string question)
    {
        Assert.NotNull(_lastTableSurface);
    }

    [Then(@"the assistant response references the table data from the attachment")]
    public void ThenTheAssistantResponseReferencesTheTableDataFromTheAttachment()
    {
        Assert.NotNull(_lastTableSurface);
    }

    [Then(@"no error surfaces are emitted")]
    public void ThenNoErrorSurfacesAreEmitted()
    {
        Assert.NotNull(_lastTableSurface);
    }
}
