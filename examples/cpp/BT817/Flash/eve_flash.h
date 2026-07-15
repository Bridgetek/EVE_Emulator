#pragma once

#include <string>

#include "bt8xxemu.h"

// Initializes a WVGA BT817 display, switches the attached Flash device to
// FULL mode, and displays the ASTC image stored in flash-817-default.bin.
bool eve_load_flash(BT8XXEMU_Emulator *emulator, std::string &errorMessage);
