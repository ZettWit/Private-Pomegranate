# Pomegranate

An offline file converter for Windows 11. Start dragging a file, hold a key, and drop it on a wheel of formats, tools and actions. Everything runs on your PC; nothing is uploaded.

| Hold while dragging | Wheel |
|---|---|
| Shift | Convert |
| Ctrl | Tools |
| Alt | Actions |

Status: v1 in progress. See [docs/SPEC.md](docs/SPEC.md) for the full design.

## Build and run

Requires the .NET 10 SDK, and Visual Studio 2022 Build Tools with the C++ workload for the right-click menu DLL.

```
powershell -File tools/fetch-engines.ps1 -Arch x64
dotnet test
dotnet run --project src/Pomegranate.App
```

`tools/package-msix.ps1 -Arch x64` builds the installable MSIX. [docs/CHECKLIST.md](docs/CHECKLIST.md) is the by-hand test list.

## License

MIT for Pomegranate's own code. Bundled engines keep their own licenses; see `THIRD-PARTY-NOTICES.md`.
