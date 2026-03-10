using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan
{
    public partial class LogService : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<LogModel> logs = new();

        public Task AddLog(LogLevel level, string text)
        {
            var log = new LogModel(level, text);

            if (MainThread.IsMainThread) InsertLog(log);
            else MainThread.BeginInvokeOnMainThread(() => InsertLog(log));

            return Task.CompletedTask;
        }

        private void InsertLog(LogModel log)
        {
            Logs.Insert(0, log);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        }
    }
}
