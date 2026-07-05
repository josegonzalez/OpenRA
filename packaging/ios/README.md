# iOS packaging

Builds the OpenRA iPad app. Requires macOS with full Xcode, the .NET SDK with the
iOS workload (`dotnet workload install ios`), cmake, and an Apple Developer team
for device signing.

## One-time setup

```sh
# Native dependencies (SDL2, OpenAL Soft, FreeType, Lua 5.1) as xcframeworks
./packaging/ios/build-native-deps.sh

# ANGLE Metal backend: either a pinned prebuilt...
ANGLE_URL=... ANGLE_SHA256=... ./packaging/ios/fetch-angle.sh

# ...or reproducibly from source (~30 minutes, several GB of tooling)
./packaging/ios/build-angle.sh
```

All artifacts land in `packaging/ios/native/` (not tracked by git).

## Build and run

```sh
# Device (replace the team/profile with your own)
dotnet build OpenRA.iOS/OpenRA.iOS.csproj -c Release -r ios-arm64 -t:Run \
    -p:CodesignKey="Apple Development" \
    -p:CodesignProvision="iOS Team Provisioning Profile: net.openra.ios"

# Simulator
dotnet build OpenRA.iOS/OpenRA.iOS.csproj -c Debug -r iossimulator-arm64
```

The app bundles the `ra`, `cnc`, and `d2k` mods; original game assets are
downloaded in-app on first run into the Files-app-visible `OpenRA` folder.

Multiplayer notes: internet play works normally. iOS restricts UDP broadcast,
so LAN games are joined via Direct Connect with the host's IP address. Official
servers only accept version-stamped release builds.
