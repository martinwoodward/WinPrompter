# WinPrompter

A modern teleprompter for Windows 11 — load a markdown script, and it scrolls beautifully on-screen with voice-driven auto-advance.

![WinUI 3](https://img.shields.io/badge/WinUI_3-blue) ![.NET 10](https://img.shields.io/badge/.NET_10-purple) ![License: MIT](https://img.shields.io/badge/License-MIT-green)

## Features

- **Markdown rendering** — load `.md` / `.txt` files or paste from clipboard
- **Smooth scrolling** — GPU-accelerated via WebView2 with adjustable speed
- **6 themes** — Classic (dark), Light, High Contrast, Green Screen, Studio, Warm
- **Voice auto-advance** — on-device speech recognition with fuzzy matching follows the speaker
- **Mirror mode** — horizontal flip for beam-splitter teleprompter setups
- **Resizable floating window** — borderless panel with drop shadow, drag edges to resize
- **Font size control** — 24pt to 120pt, adjust with keyboard or settings
- **Window opacity** — semi-transparent overlay mode
- **Cue markers** — `<!-- pause 3s -->` in your script auto-pauses scrolling
- **Section jump** — table of contents from markdown headings
- **Countdown timer** — 3-2-1 countdown before scrolling starts
- **WPM display** — real-time words-per-minute readout
- **Progress bar** — visual scroll progress indicator
- **Drag & drop** — drop a file onto the window to load it
- **Recent files** — quick access to previously opened scripts
- **Fullscreen mode** — expand to fill the entire display

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Space` | Play / Pause |
| `Escape` | Stop & reset scroll position |
| `Ctrl` + `+` | Increase font size |
| `Ctrl` + `-` | Decrease font size |
| `Alt` + `↑` | Increase scroll speed |
| `Alt` + `↓` | Decrease scroll speed |
| `Ctrl` + `O` | Open file dialog |
| `Ctrl` + `V` | Paste script from clipboard |
| `F` / `F11` | Toggle fullscreen |
| `M` | Toggle mirror mode |
| `V` | Toggle voice auto-advance |
| `[` | Decrease window opacity |
| `]` | Increase window opacity |

## Getting Started

### Prerequisites

| Requirement | Version |
|-------------|---------|
| Windows | 10 (1809+) or 11 |
| .NET SDK | 10.0 or later |
| Windows App SDK | 1.8+ (pulled via NuGet) |
| WebView2 Runtime | Pre-installed on Windows 11; [download for Windows 10](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |

### Clone & Build

```powershell
git clone https://github.com/OWNER/winprompter.git
cd winprompter

# Restore dependencies
dotnet restore WinPrompter.csproj -p:Platform=x64

# Build (x64 — also supports arm64)
dotnet build WinPrompter.csproj -p:Platform=x64 -c Release

# Run
dotnet run --project WinPrompter.csproj -p:Platform=x64
```

> **Note:** You must specify `-p:Platform=x64` (or `arm64`). The project does not support AnyCPU.

### Build for ARM64

```powershell
dotnet build WinPrompter.csproj -p:Platform=arm64 -c Release
dotnet run --project WinPrompter.csproj -p:Platform=arm64
```

### Create an MSIX Package (for sideloading or Store)

```powershell
dotnet publish WinPrompter.csproj -p:Platform=x64 -c Release `
  -p:WindowsPackageType=MSIX `
  -p:AppxBundle=Never `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageDir=".\AppPackages\"
```

The `.msix` file will be in `.\AppPackages\`. For signing and Store submission, see [docs/STORE_DEPLOYMENT.md](docs/STORE_DEPLOYMENT.md).

## CI/CD

### Continuous Integration

Every push to `main` and every pull request triggers the CI workflow:

```
.github/workflows/ci.yml
```

- Builds for **x64** and **arm64** on `windows-latest`
- Uploads build artifacts for the x64 Release configuration

### Release & Store Deployment

Pushing a version tag triggers the release workflow:

```
.github/workflows/release.yml
```

- Builds signed MSIX packages for x64 and ARM64
- Creates a GitHub Release with the `.msix` files attached
- Optionally submits to the Microsoft Store (when `ENABLE_STORE_SUBMIT` is `true`)

```powershell
# Create a release
git tag v1.0.0
git push origin v1.0.0
```

### Setting Up Secrets

The release workflow requires several GitHub secrets for signing and Store submission. Full step-by-step instructions with copy-paste `gh` commands are in:

📄 **[docs/STORE_DEPLOYMENT.md](docs/STORE_DEPLOYMENT.md)**

Quick summary of required secrets:

| Secret | Purpose |
|--------|---------|
| `STORE_CERTIFICATE_BASE64` | Base64-encoded `.pfx` signing cert |
| `STORE_CERTIFICATE_PASSWORD` | Password for the `.pfx` |
| `STORE_PUBLISHER_ID` | Publisher CN from Partner Center |
| `STORE_PACKAGE_IDENTITY` | Package name from Partner Center |

Additional secrets for automatic Store submission (optional):

| Secret | Purpose |
|--------|---------|
| `STORE_PRODUCT_ID` | Product ID from Partner Center |
| `AZURE_TENANT_ID` | Azure AD tenant |
| `AZURE_CLIENT_ID` | Azure AD app client ID |
| `AZURE_CLIENT_SECRET` | Azure AD app secret |

## Voice Auto-Advance

WinPrompter uses Windows on-device speech recognition to follow along as you speak and automatically advance the script.

### How it works

1. Your script is pre-processed into a word index with character offsets
2. The speech recognizer captures continuous dictation on-device (no cloud)
3. A fuzzy matcher (Levenshtein distance ≤ 2) slides a window over the script
4. When consecutive matches are found, the scroll position advances to that point

### Requirements

- A microphone
- Windows speech language pack installed for your language
  - **Settings → Time & Language → Speech → Add languages**

Press **V** or click the mic button in the toolbar to toggle voice mode.

## Design

WinPrompter uses a **floating borderless window** with a distinctive corner style:
- **Square corners at top** — flush with screen edge when positioned at top
- **Rounded corners at bottom** — soft, modern appearance

The asymmetric corner styling is achieved using XAML's `CornerRadius="0,0,8,8"` property, providing a professional look while maintaining compatibility with Windows visual effects (drop shadows, acrylic backgrounds).

For details on the design decision and alternative approaches, see [docs/EXECUTIVE_SUMMARY.md](docs/EXECUTIVE_SUMMARY.md).

## Project Structure

```
WinPrompter/
├── .github/workflows/    # CI and release workflows
├── Assets/
│   └── Web/              # WebView2 content (HTML/CSS/JS)
├── docs/                 # Documentation
│   ├── STORE_DEPLOYMENT.md              # Store submission guide
│   ├── EXECUTIVE_SUMMARY.md             # Corner styling research summary
│   ├── ROUNDED_CORNERS_RESEARCH.md      # Detailed analysis of options
│   ├── ROUNDED_CORNERS_IMPLEMENTATION.md # Code samples
│   ├── CORNER_RADIUS_VISUAL_REFERENCE.md # Visual guide
│   ├── CORNER_STYLING_COMPARISON.md      # Technical comparison
│   └── QUICK_START_CORNERS.md            # Developer quick guide
├── Helpers/
│   ├── FuzzyMatcher.cs   # Levenshtein sliding-window matcher
│   ├── WebViewBridge.cs  # C# ↔ JavaScript interop
│   └── WindowHelper.cs   # Win32 borderless window setup
├── Models/
│   └── ScriptWordIndex.cs
├── Services/
│   ├── MarkdownService.cs
│   ├── SettingsService.cs
│   ├── SpeechService.cs
│   └── VoiceAdvanceService.cs
├── ViewModels/
│   └── MainViewModel.cs
├── MainWindow.xaml        # Main UI
├── MainWindow.xaml.cs     # App logic
└── WinPrompter.csproj
```

## Technology

| Component | Technology |
|-----------|-----------|
| UI Framework | WinUI 3 / Windows App SDK 1.8 |
| Rendering | WebView2 (Chromium-based) |
| Markdown | [Markdig](https://github.com/xoofx/markdig) |
| Speech | Windows.Media.SpeechRecognition (on-device) |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |
| Packaging | MSIX |

## License

MIT — see [LICENSE](LICENSE) for details.
