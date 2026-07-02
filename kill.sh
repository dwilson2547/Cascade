#!/bin/bash
# Kills any running Cascade game instance (see startup.sh for how it's launched).
if pkill -9 -f "Godot.*--path.*games/Cascade"; then
    echo "Cascade killed."
else
    echo "No running Cascade instance found."
fi
