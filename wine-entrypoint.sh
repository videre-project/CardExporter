#!/usr/bin/env bash
set -euo pipefail

export PATH="/opt/wine/bin:${PATH}"
export DOTNET_ROOT="${DOTNET_ROOT:-C:\\dotnet}"
export WINEDEBUG="${WINEDEBUG:--all}"

# MTGO's WPF audio manager queries Windows Core Audio's ISimpleAudioVolume.
# The headless ALSA driver can expose a partial COM surface under Wine, which
# crashes MTGO during startup. The pulse driver avoids that broken path even
# when there is no real audio device attached.
WINE_AUDIO_DRIVER="${WINE_AUDIO_DRIVER:-pulse}"
if command -v wine >/dev/null 2>&1; then
  wine reg add "HKEY_CURRENT_USER\\Software\\Wine\\Drivers" \
    /v "Audio" /t REG_SZ /d "${WINE_AUDIO_DRIVER}" /f >/dev/null 2>&1 || true
  wineserver -k >/dev/null 2>&1 || true
fi

exec "$@"
