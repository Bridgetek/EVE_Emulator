#pragma once

#include <string>

#include "bt8xxemu.h"

// Initializes the BT820 1280x720 display, attaches the mounted SD card,
// loads bicycle.anim.reloc from the filesystem, and draws animation frame 1.
bool eve_load_sdcard(BT8XXEMU_Emulator *emulator, std::string &errorMessage);
