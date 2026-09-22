#!/bin/sh
# Build the C++ sim bridge and drop it where Unity loads it from.
# Usage: ./unity/build_dylib.sh   (from repo root)
set -e
cd "$(dirname "$0")/.."
cmake -B unity/build -S unity -DCMAKE_BUILD_TYPE=Release >/dev/null
cmake --build unity/build >/dev/null
cp unity/build/libjv_unity.dylib unity-project/Assets/Plugins/
echo "OK: unity-project/Assets/Plugins/libjv_unity.dylib"
