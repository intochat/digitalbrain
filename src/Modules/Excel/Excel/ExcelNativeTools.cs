using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.VisualBasic.FileIO;

namespace DigitalBrain.Excel;

internal sealed class ExcelNativeTools(IGrainFactory grains, INeuronInvoker invoker)
{
    internal AIFunction Create()
    {
        Task<string> ShowSpreadsheet(
            [Description("The current chat, exactly as stated in the conversation context")] string chatName,
            [Description("Short spreadsheet title")] string title,
            [Description("Sheet as comma-separated rows, with column headers in the first row; quote cells containing commas, quotes, or line breaks")] string csv,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ShowSpreadsheetAsync(chatName, title, csv, cancellationToken);

        return AIFunctionFactory.Create(ShowSpreadsheet, new AIFunctionFactoryOptions
        {
            Name = "show_spreadsheet",
            Description = "Show a spreadsheet as a live card in the chat when the person asks to see or work with tabular data.",
        });
    }

    private async Task<string> ShowSpreadsheetAsync(string chatName, string title, string csv, CancellationToken cancellationToken)
    {
        if (!NeuronId.TryParse(chatName, out var chat) || chat.Type != UIVocabulary.ChatType)
        {
            return "chatName must be a uichat neuron. Copy the current chat exactly from the Chat: line in the conversation context (for example, uichat:desk).";
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            return "title must not be blank.";
        }
        if (string.IsNullOrWhiteSpace(csv))
        {
            return "csv must contain a header row.";
        }

        try
        {
            using var input = new StringReader(csv);
            using var parser = new TextFieldParser(input)
            {
                TextFieldType = FieldType.Delimited,
                Delimiters = [","],
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false,
            };
            var columns = parser.ReadFields();
            if (columns is null || columns.Length == 0)
            {
                return "csv must contain a header row.";
            }
            if (columns.Length > SheetGrid.MaxColumns)
            {
                return $"csv must have at most {SheetGrid.MaxColumns} columns.";
            }
            var rows = new List<ExcelRow>();
            while (!parser.EndOfData)
            {
                var cells = parser.ReadFields();
                if (cells is null)
                {
                    break;
                }
                if (cells.Length > SheetGrid.MaxColumns)
                {
                    return $"csv must have at most {SheetGrid.MaxColumns} columns.";
                }
                if (cells.Length > columns.Length)
                {
                    return "csv rows must not have more cells than the header row.";
                }
                if (rows.Count == SheetGrid.MaxRows)
                {
                    return $"csv must have at most {SheetGrid.MaxRows} data rows after the header.";
                }
                rows.Add(new ExcelRow(cells));
            }

            var grid = SheetGrid.Normalize(new ExcelState(title, "Sheet1", columns, rows));
            var name = $"sheet-{Guid.NewGuid():N}"[..14];
            var neuron = new NeuronId("sheet", name);

            // Connect first so the apply reaction has a chat listener for its card.
            await grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(chat, ExcelSignals.SheetChanged).ConfigureAwait(false);
            await invoker.InvokeAsync(neuron, "sheet", "apply",
                JsonSerializer.SerializeToElement(new ApplySheetEdit(CommandId.New(), grid, null),
                    ExcelJson.Default.ApplySheetEdit), cancellationToken).ConfigureAwait(false);

            return $"Spreadsheet '{grid.Title}' is now showing in the chat as card '{name}'.";
        }
        catch (MalformedLineException)
        {
            return "csv must use valid comma-separated rows with correctly closed and escaped quotes.";
        }
        catch (Exception error)
        {
            return $"show_spreadsheet failed: {error.GetType().Name}: {error.Message}";
        }
    }
}
