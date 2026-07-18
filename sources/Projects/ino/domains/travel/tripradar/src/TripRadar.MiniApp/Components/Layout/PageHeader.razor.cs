using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TripRadar.MiniApp.Components.Layout;

public partial class PageHeader
{
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public bool ShowBack { get; set; }
    [Parameter] public string? BackRoute { get; set; }
    [Parameter] public RenderFragment? RightContent { get; set; }
    [Parameter] public bool ShowBranding { get; set; }
    [Parameter] public string? Subtitle { get; set; }

    protected override void OnInitialized() => TopBar.OnChanged += StateHasChanged;

    private async Task GoBack()
    {
        if (BackRoute is not null)
            Nav.NavigateTo(BackRoute);
        else
            await JS.InvokeVoidAsync("history.back");
    }

    public void Dispose() => TopBar.OnChanged -= StateHasChanged;
}
