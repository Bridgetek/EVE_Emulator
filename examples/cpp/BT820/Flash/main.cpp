#include <windows.h>

#include <array>
#include <cstddef>
#include <cwchar>
#include <filesystem>
#include <iostream>
#include <iterator>
#include <string>
#include <system_error>

#include "bt8xxemu.h"
#include "eve_flash.h"

int wmain()
{
    std::array<wchar_t, 32768> executablePath{};
    const DWORD pathLength = GetModuleFileNameW(
        nullptr, executablePath.data(), static_cast<DWORD>(executablePath.size()));
    if (pathLength == 0 || pathLength == executablePath.size())
    {
        std::cerr << "Unable to resolve the executable path.\n";
        return 1;
    }

    const std::filesystem::path flashPath =
        std::filesystem::path(executablePath.data()).parent_path() /
        L"flash-820-default.bin";
    std::error_code fileError;
    if (!std::filesystem::is_regular_file(flashPath, fileError))
    {
        std::wcerr << L"Flash image was not found: " << flashPath << L'\n';
        return 1;
    }

    BT8XXEMU_FlashParameters flashParameters{};
    BT8XXEMU_Flash_defaults(BT8XXEMU_VERSION_API, &flashParameters);
    if (flashPath.native().size() >= std::size(flashParameters.DataFilePath) ||
        wcscpy_s(flashParameters.DataFilePath,
                 std::size(flashParameters.DataFilePath),
                 flashPath.c_str()) != 0)
    {
        std::cerr << "The Flash image path is too long.\n";
        return 1;
    }
    flashParameters.SizeBytes = 8 * 1024 * 1024;

    BT8XXEMU_Flash *flash =
        BT8XXEMU_Flash_create(BT8XXEMU_VERSION_API, &flashParameters);
    if (!flash)
    {
        std::cerr << "Unable to create the Flash emulator.\n";
        return 1;
    }

    BT8XXEMU_EmulatorParameters emulatorParameters{};
    BT8XXEMU_defaults(BT8XXEMU_VERSION_API,
                      &emulatorParameters,
                      BT8XXEMU_EmulatorBT820);
    emulatorParameters.Flash = flash;

    BT8XXEMU_Emulator *emulator = nullptr;
    std::cout << BT8XXEMU_version() << '\n';
    std::cout << "Launching the BT820 emulator with flash-820-default.bin."
              << std::endl;
    BT8XXEMU_run(BT8XXEMU_VERSION_API, &emulator, &emulatorParameters);
    if (!emulator)
    {
        std::cerr << "Unable to launch the EVE emulator.\n";
        BT8XXEMU_Flash_destroy(flash);
        return 1;
    }

    std::string displayError;
    if (!eve_load_flash(emulator, displayError))
    {
        std::cerr << displayError << '\n';
        BT8XXEMU_stop(emulator);
        BT8XXEMU_destroy(emulator);
        BT8XXEMU_Flash_destroy(flash);
        return 1;
    }
    std::cout << "Displaying the 288x163 RGB8 image copied from Flash."
              << std::endl;

    while (BT8XXEMU_isRunning(emulator))
        Sleep(50);

    BT8XXEMU_stop(emulator);
    BT8XXEMU_destroy(emulator);
    BT8XXEMU_Flash_destroy(flash);
    return 0;
}
