#pragma once

#include <string>

#include "bt8xxemu.h"

// Initializes the BT820 1280x720 render target, switches the attached Flash
// device to FULL mode, copies an RGB8 image from Flash to RAM_G, and displays it.
bool eve_load_flash(BT8XXEMU_Emulator *emulator, std::string &errorMessage);
