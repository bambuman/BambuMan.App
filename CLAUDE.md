# Repository Defaults

## Environment
- Development machine: **Windows 11**
- .NET 10.0 C#
- Nullable is set to **enable**

## Technology Stack
- **MAUI 10.0** — Cross-platform mobile framework (Android primary, iOS configured)
- **Windows Forms** — Desktop companion app
- **CommunityToolkit.Mvvm** — MVVM pattern with `[ObservableProperty]`, `ObservableObject`
- **CommunityToolkit.Maui** + **Markup** — MAUI extensions and C# markup
- **UraniumUI** — Material design components (FontAwesome, MaterialSymbols icons)
- **RestSharp** — HTTP client (BambuMan.Shared)
- **Newtonsoft.Json** — JSON serialization
- **Serilog** — Structured logging (with Sentry and Xamarin sinks)
- **Sentry** — Error tracking and performance monitoring
- **Polly** — Resilience (retry, circuit breaker, timeout) via `Microsoft.Extensions.Http.Polly`
- **PCSC** — Smart card / NFC reader (Desktop)
- **BarcodeScanning.Native.Maui** — Barcode scanning
- **xUnit v3** — Unit testing with coverlet code coverage

## Solution Structure
Solution file: `src/BambuMan.sln`

| Project | Type | Description |
|---|---|---|
| `BambuMan` | MAUI App (Android) | Mobile application — Pages, ViewModels, platform code |
| `BambuMan.Desktop` | WinForms App | Windows desktop companion with PCSC/NFC support |
| `BambuMan.Shared` | Class Library | Shared business logic, services, models, NFC utilities |
| `SpoolMan.Api` | Class Library | OpenAPI-generated REST client for Spoolman API |
| `BambuMan.Shared.Test` | xUnit Test | Tests for BambuMan.Shared |
| `SpoolMan.Api.Test` | xUnit Test | Tests for SpoolMan.Api |

### Key Directories
- `BambuMan/UI/` — Pages and ViewModels organized by feature (Main, Settings, Logs, Scan, Consent)
- `BambuMan/Shared/` — Cross-platform services (LogService, InventoryService, AppContainer)
- `BambuMan/Platforms/` — Platform-specific implementations (Android, iOS)
- `BambuMan.Shared/Nfc/` — NFC abstractions and utilities
- `BambuMan.Shared/Models/` — Shared data models
- `SpoolMan.Api/Api/` — Generated API endpoint classes
- `SpoolMan.Api/Model/` — Generated API models

### Architecture
- **MVVM** with CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`)
- **Dependency Injection** via `MauiProgram.cs` — singletons for stateful services, transients for pages/viewmodels
- **Event-driven** communication between `SpoolmanManager` and UI via delegates
- **Global service locator** via `AppContainer.Services` for runtime access


## Naming Conventions
- **PascalCase** for public members
- **camelCase** for private fields (no underscore prefix: `field` not `_field`)
- `var` wherever possible
- **MethodAsync** suffix for async methods
- **I** prefix for interfaces (e.g., `ISettingsService`)

## Code Style
- No curly braces for single-line if/else:
  ```csharp
  if (condition) field = "test";
  else field = "live";
  ```
- Use `#region` / `#endregion` for organizing entity sections (Properties, Foreign Keys, Collections, Methods)
- Services: interface + implementation in the same file (e.g., `ISettingsService` + `SettingsService`)
- Constructor injection with `this.field = field` pattern
- Use `internal` for controller-level fields where applicable
- File-scoped namespaces allowed (mixed usage in codebase)
- `async void` methods (e.g., event handlers, `OnAppearing`) must wrap their body in a `try/catch` — unhandled exceptions in `async void` crash the app

## Git
- Commit messages must **not** include `Co-Authored-By` lines

## Code Generation Rules
- Keep the code style consistent with existing examples in the repository
- Use NuGet libraries where available — do not create custom solutions when a library exists
- Keep generated code as simple as possible
- Follow professional standards
- Ask the user if anything is unclear or missing before proceeding
- Minimize web queries — if a NuGet package source is needed, ask the user to download it
- Do not generate code unless explicitly instructed