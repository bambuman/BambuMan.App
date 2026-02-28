using BambuMan.Interfaces;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Logs;

public partial class LogsPage
{
    private readonly LogsPageViewModel viewModel;
    private readonly ILogger<LogsPage> logger;
    private readonly LogService logService;
    private readonly IInvokeIndent invokeIndent;

    public LogsPage(LogsPageViewModel viewModel, ILogger<LogsPage> logger, LogService logService, IInvokeIndent invokeIndent)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        this.logger = logger;
        this.logService = logService;
        this.invokeIndent = invokeIndent;
        BindingContext = viewModel;
    }

    private void ClearLogs_OnClicked(object? sender, EventArgs e)
    {
        logService.Logs.Clear();
    }
    
    private async void BackToMain_OnClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }

    private async void EmailLogs_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            var logs = $"{string.Join("\r\n", viewModel.Logs.Select(x => x.Content))}\r\n\r\n";
            await invokeIndent.SendEmail("bambuman.app@gmail.com", "Bambu logs", logs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in EmailLogs_OnClicked");
        }
    }
}