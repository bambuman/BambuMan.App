using Android.App;
using Android.Runtime;
using BambuMan.Implementations;
using BambuMan.Interfaces;
using BambuMan.Shared.Interfaces;
using BambuMan.Utils;
using Serilog;

namespace BambuMan
{
    [Application]
    public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public static void SetupImplementations(IServiceCollection services)
        {
            services.AddSingleton<IToneGenerator, AndroidToneGenerator>();
            services.AddTransient<IInvokeIndent, AndroidInvokeIndent>();
        }

        public static void InitBuildVersion()
        {
            var context = Context;
            var appInfo = PackageUtils.GetPackageInfo(context.PackageName);

            BuildVersionModel.CurrentBuildVersion = appInfo?.VersionName;
            BuildVersionModel.PackageFullName = appInfo?.PackageName;
        }

        public static void SetupSerilog()
        {
            var logConfig = new LoggerConfiguration();
            logConfig.WriteTo.Debug();

            var appInfo = PackageUtils.GetPackageInfo(Context.PackageName);

            MauiProgram.SetupSerilog(logConfig, appInfo?.VersionName, appInfo?.PackageName);
        }
    }
}
