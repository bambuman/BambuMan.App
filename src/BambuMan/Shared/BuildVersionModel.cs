using BambuMan.Shared.Enums;

namespace BambuMan
{
    public static class BuildVersionModel
    {
#if BUILD_TYPE_LIVE
        public const string SentryEnvironment = "Production";
        public const BuildModeType BuildMode = BuildModeType.Live;
#else
        public const string SentryEnvironment = "Development";
        public const BuildModeType BuildMode = BuildModeType.Development;
        
#endif
        
        public static Guid SessionId { get; } = Guid.NewGuid();

        public static string? CurrentBuildVersion { get; set; }

        public static string? Name { get; set; }

        public static string? PackageFullName { get; set; }
    }
}
