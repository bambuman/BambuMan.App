using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared.Models
{
    public class LogModel(LogLevel level, string text)
    {
        public LogLevel Level { get; } = level;

        public string Content { get; } = $"[{DateTime.Now:HH:mm:ss}]: {text}";
    }
}
