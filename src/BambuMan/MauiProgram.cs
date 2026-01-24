using BambuMan.Shared;
using BambuMan.UI.Consent;
using BambuMan.UI.Logs;
using BambuMan.UI.Main;
using BambuMan.UI.Scan;
using BambuMan.UI.Settings;
using BarcodeScanning;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using UraniumUI;
using MainPageViewModel = BambuMan.UI.Main.MainPageViewModel;

namespace BambuMan
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
#if ANDROID
            MainApplication.InitBuildVersion();
            MainApplication.SetupSerilog();
#endif
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit(options =>
                {
                    options.SetShouldSuppressExceptionsInBehaviors(true);
                    options.SetShouldSuppressExceptionsInConverters(true);
                })
                .UseMauiCommunityToolkitMarkup()
                .UseBarcodeScanning()
                .UseUraniumUI()
                .UseUraniumUIMaterial()
                .UseSentry(options =>
                {
                    options.Dsn = "https://1881c4151cf22d2ff6c3d92fa5d68d87@o4509141125365760.ingest.de.sentry.io/4509141131198544";
                    options.Environment = BuildVersionModel.SentryEnvironment;
                    options.Release = $"{BuildVersionModel.PackageFullName}@{BuildVersionModel.CurrentBuildVersion}";

                    // Set TracesSampleRate to 1.0 to capture 100% of transactions for tracing.
                    // We recommend adjusting this value in production.
                    options.TracesSampleRate = 0.1;

                    options.AutoSessionTracking = true;

                    options.AttachScreenshot = true;
#if DEBUG
                    options.Debug = true;
#endif
                    // Other Sentry options can be set here.
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFontAwesomeIconFonts();
                    fonts.AddMaterialSymbolsFonts();
                });

            var services = builder.Services;
            AddUi(services);
            AddServices(services);



#if ANDROID
            MainApplication.SetupImplementations(services);
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Logging.AddSerilog(Log.Logger, dispose: true);

            var app = builder.Build();

            AppContainer.Services = app.Services;

            return app;
        }

        private static void AddUi(IServiceCollection services)
        {
            services.AddSingleton<AppShell>();

            services.AddTransient<MainPage>();
            services.AddSingleton<MainPageViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<SettingsPageViewModel>();
            services.AddTransient<LogsPageViewModel>();
            services.AddTransient<ScanPage>();
            services.AddSingleton<LogService>();
            services.AddSingleton<TagApiService>();
            services.AddTransient<TagUploadConsentPopup>();
            services.AddHttpClient();
        }

        private static void AddServices(IServiceCollection services)
        {
            services.AddSingleton<SpoolmanManager>();
        }

        public static void SetupSerilog(LoggerConfiguration loggerConfiguration, string? version, string? packageName, string? deviceId)
        {
            loggerConfiguration = loggerConfiguration
                .Enrich.WithProperty("PackageName", packageName)
                .Enrich.WithProperty("Version", version)
                .Enrich.WithProperty("DeviceId", deviceId)
                .Enrich.FromGlobalLogContext()
                .Filter.ByExcluding(e => e.Properties.TryGetValue("SourceContext", out var value) && value.ToString().Contains("Microsoft.Maui.Controls.Xaml.Diagnostics.BindingDiagnostics"))
                .Filter.ByExcluding(e => e.Exception?.Message.Contains("Font asset not found") ?? false)
                .WriteTo.Sentry(o =>
                {
                    o.InitializeSdk = false;

                    // Debug and higher are stored as breadcrumbs (default is Information)
#if DEBUG
                    o.MinimumBreadcrumbLevel = LogEventLevel.Debug;
#else
                    o.MinimumBreadcrumbLevel = LogEventLevel.Information;
#endif

                    // Warning and higher is sent as event (default is Error)
                    o.MinimumEventLevel = LogEventLevel.Warning;
                });

            Log.Logger = loggerConfiguration.CreateLogger();
        }
    }
}
