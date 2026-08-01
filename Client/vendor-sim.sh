#!/bin/sh
# Rebuilds Sim and Net and drops the assemblies into Assets/Plugins.
#
# The direction of the dependency is enforced by this being a one-way copy of an
# assembly that dotnet compiled: WordCraft.Sim.dll and WordCraft.Net.dll cannot
# reference UnityEngine because UnityEngine is not on the path when they build.
#
# ponytail: the two assemblies are committed binaries, so they go stale the
# moment Sim or Net changes without this script being run. Upgrade path when
# that bites: point Sim.csproj/Net.csproj OutputPath here and stop committing
# them, at the cost of the Unity project needing a dotnet build before it opens.
set -e
cd "$(dirname "$0")/.."
dotnet build -c Release Net/Net.csproj
cp Sim/bin/Release/netstandard2.1/WordCraft.Sim.dll Client/Assets/Plugins/
cp Net/bin/Release/netstandard2.1/WordCraft.Net.dll Client/Assets/Plugins/
