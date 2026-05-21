#!/bin/bash
echo "Building DepotDL.CLI..."
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "[ERROR] Build failed!"
    exit 1
fi
echo "[SUCCESS] Build succeeded!"
echo "Executable is located in: bin/Release/net9.0/DepotDL.CLI"
