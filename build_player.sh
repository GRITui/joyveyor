#!/bin/sh
# JoyVeyor v1.0 — standalone macOS player build (S9 release packaging).
# Usage: ./build_player.sh   (from repo root)
#
# Steps:
#   1. Rebuild the C++ sim bridge dylib (gitignored — a clean checkout
#      must regenerate it before Unity can load the sim).
#   2. Set Player Settings (product name, company, icon, bundle id) via
#      the headless Unity editor.
#   3. Build the macOS Standalone player to dist/JoyVeyor.app.
set -e
cd "$(dirname "$0")"

UNITY="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
PROJ="unity-project"
OUT="dist"

# 1. C++ bridge (sh, not ./ — the script is mode 100644 in git)
sh unity/build_dylib.sh

# 2+3. Player settings + build
rm -rf "$OUT"
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJ" \
  -executeMethod ReleaseBuild.Run \
  -buildTarget "Standalone" \
  -logFile /tmp/joyveyor_build.log
echo "OK: $OUT/JoyVeyor.app"
