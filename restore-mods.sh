#!/usr/bin/env bash
# Put RackColors + RealCustomers back and remove the profiler.
# Run with the game CLOSED - loaded DLLs are file-locked.
set -e
MODS="/c/Program Files (x86)/Steam/steamapps/common/Data Center/Mods"
DIS="/c/Users/user/datacenter-mods-disabled"

if tasklist //FI "IMAGENAME eq Data Center.exe" 2>/dev/null | grep -qi "data center"; then
  echo "Data Center is running - close it first." >&2
  exit 1
fi

for f in RackColors RealCustomers; do
  cp "$DIS/$f.dll" "$MODS/$f.dll"
  echo "restored $f.dll"
done

[ -f "$MODS/DCProfiler.dll" ] && rm "$MODS/DCProfiler.dll" && echo "removed DCProfiler.dll"
echo "--- Mods/ now ---"
ls -1 "$MODS"
