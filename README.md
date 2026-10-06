# RinIME for Android

C# / .NET 10 Android IME with offline kana-kanji conversion using Mozc dictionary data. Includes cursor editing, partial candidate confirmation, and raw-text confirmation with Enter (version 1.6).

In half-width alphabet mode, tap Shift once for the next capital, twice for Caps Lock, and again to unlock. Labels and flick previews follow the input case. Symbols, deletion, space and Enter retain pending Shift; switching input modes or ending the input session resets it. The O vowel key (お in romaji mode) now has a half-width `@` down flick.

## Build and test

Install .NET 10. For APK builds also install the Android workload, Android SDK 36 and JDK 21. In PowerShell:

```powershell
./tools/PrepareDictionary.ps1
dotnet run --project tests/OfflineChecks -- NyaIme/Assets/offline-dictionary.gz
dotnet build NyaIme/NyaIme.csproj -c Release -m:1
```

The dictionary script downloads the pinned official Mozc sources and verifies each input SHA256 against the included provenance before generating the asset. Generated dictionaries, APKs, signing keys, local settings and logs are excluded from Git. No GitHub Release is published.

GitHub Actions regenerates the dictionary and runs the CPU regression suite. Android integration has been checked by local Release builds and mock input connections; the public copy has not been tested on a device.

## Online conversion

This public source contains no API key. `KanaKanjiClient` accepts a runtime key-provider function or reads `OPENAI_API_KEY`. Without a key, it makes no HTTP request and retains local candidates. The Android service currently uses the default constructor: connecting an app-private credential provider or a server-side proxy remains necessary to enable online conversion in this public build. Do not hardcode a key or commit credentials. Tests never submit user input to an external API.

## Notices

Mozc data notices, license and exact provenance are in `NyaIme/Assets`. This repository does not assign a new license to the application's own source. See `NyaIme/OFFLINE-CONVERSION.md` for conversion behavior and limitations.
