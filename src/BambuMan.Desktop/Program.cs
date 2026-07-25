namespace BambuMan.Desktop
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Safety net only — a startup fault used to kill the process with no window and no message, leaving nothing
            // but an "Exception code: 0xe0434352" entry in the event log. Faults still need fixing at their source.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ReportFatal(ex);
            }
        }

        private static void ReportFatal(Exception? ex)
        {
            if (ex == null) return;

            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BambuMan", "crash.log");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]: {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Nothing left to do if even the crash log can't be written.
            }

            MessageBox.Show($"{ex.Message}{Environment.NewLine}{Environment.NewLine}Details were written to:{Environment.NewLine}{logPath}", "BambuMan crashed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
