using System.ComponentModel;

namespace BambuMan.Shared.Enums;

public enum ManagerStatusType
{
    [Description("Initializing ...")]
    Initializing = 0,

    [Description("Api connected")]
    ApiConnected = 10,

    [Description("Checking default settings")]
    CheckingDefaults = 20,

    [Description("Default settings ok")]
    DefaultsOk = 30,

    [Description("All external filaments loaded")]
    AllExternalFilamentsLoaded = 50,

    [Description("Ready to inventory fillament")]
    Ready = 200,

    [Description("Error")]
    Error = 500,

    [Description("Api url missing")]
    ApiUrlMissing = 501,

    [Description("Can't connect to api, check if url is correct!")]
    CantConnectToApi = 502,
}
