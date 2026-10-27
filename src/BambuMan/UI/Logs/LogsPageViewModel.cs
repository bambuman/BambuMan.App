using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.Logs
{
    public partial class LogsPageViewModel(LogService logService) : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<LogModel> logs = logService.Logs;
    }
}
