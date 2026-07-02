#!/bin/bash
# Launches the Cascade game as a standalone process (not the Godot editor's
# embedded Game panel, which silently drops input under WSL2 -- see
# ecosystem_sim/docs/issues/2026_06_30_godot_wsl2_embedded_panel_input.md).
#
# Uses --rendering-method gl_compatibility: the default Vulkan/Forward+
# renderer falls back to a software (llvmpipe) implementation in this
# environment and produces no visible output, while gl_compatibility picks up
# the real GPU via WSL's D3D12 translation layer and renders correctly.
GODOT_BIN="Godot_v4.7-stable_mono_linux.x86_64"  # resolved from PATH
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_FILE="/tmp/cascade_game.log"

"$PROJECT_DIR/kill.sh" >/dev/null 2>&1

nohup "$GODOT_BIN" --rendering-method gl_compatibility --path "$PROJECT_DIR" > "$LOG_FILE" 2>&1 &
disown
echo "Cascade launched (PID $!). Log: $LOG_FILE"
