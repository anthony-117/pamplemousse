# Development Environment

Checked on 2026-10-26.

| Tool | Status | Action |
|---|---|---|
| .NET SDK | 9.0.305 only | Install .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10` |
| IDE | Visual Studio 2022 (no .NET 10 support), VS Code 1.126 | Use Visual Studio 2026, or VS Code + C# Dev Kit + Flutter/Dart extensions |
| Flutter / Dart | Not installed | Install Flutter SDK to `C:\dev\flutter` (outside OneDrive), add `C:\dev\flutter\bin` to PATH, run `flutter doctor` |
| Android SDK + Android Studio | Installed | `flutter doctor --android-licenses` |
| Java | JDK 26 (too new for Gradle) | `flutter config --jdk-dir "C:\Program Files\Android\Android Studio\jbr"` |
| Docker | 29.6 ✅ | Required by Aspire |
| PostgreSQL | 17 local ✅ | pgvector needed (or Docker/Neon) |
| Chrome | ✅ | Flutter web |
| GitHub CLI | Missing | Optional: `winget install GitHub.cli` |
| Node | Installed via nvm, not active | Only for some MCP tooling (MCP Inspector) |

## Verification
```
dotnet --list-sdks        # shows 10.x
flutter doctor -v         # all green except optional items
docker info
```
