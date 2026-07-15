#include <windows.h>

#include <array>
#include <filesystem>
#include <iostream>
#include <string>
#include <system_error>

#include "bt8xxemu.h"
#include "eve_sdcard.h"

namespace
{
void destroyEmulator(BT8XXEMU_Emulator *emulator, bool sdCardMounted)
{
    if (!emulator)
        return;

    if (sdCardMounted)
        BT8XXEMU_ejectSDCard(emulator);
    BT8XXEMU_stop(emulator);
    BT8XXEMU_destroy(emulator);
}
} // namespace

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

    const std::filesystem::path outputDirectory =
        std::filesystem::path(executablePath.data()).parent_path();
    const std::filesystem::path sdFolder = outputDirectory / L"sd";
    const std::filesystem::path animationPath =
        sdFolder / L"bicycle.anim.reloc";

    std::error_code fileError;
    if (!std::filesystem::is_regular_file(animationPath, fileError))
    {
        std::wcerr << L"SD-card asset was not found: " << animationPath << L'\n';
        return 1;
    }

    BT8XXEMU_EmulatorParameters emulatorParameters{};
    BT8XXEMU_defaults(BT8XXEMU_VERSION_API,
                      &emulatorParameters,
                      BT8XXEMU_EmulatorBT820);

    BT8XXEMU_Emulator *emulator = nullptr;
    std::cout << BT8XXEMU_version() << '\n';
    std::cout << "Launching the BT820 emulator without Flash." << std::endl;
    BT8XXEMU_run(BT8XXEMU_VERSION_API, &emulator, &emulatorParameters);
    if (!emulator)
    {
        std::cerr << "Unable to launch the EVE emulator.\n";
        return 1;
    }

    const ptrdiff_t sdCardSize = BT8XXEMU_insertSDCardFolder(
        emulator, sdFolder.c_str(), 64 * 1024 * 1024, false);
    if (sdCardSize <= 0)
    {
        std::wcerr << L"Unable to mount the SD-card folder: " << sdFolder << L'\n';
        destroyEmulator(emulator, false);
        return 1;
    }
    std::cout << "Mounted the SD-card folder ("
              << sdCardSize << " bytes)." << std::endl;

    std::string displayError;
    if (!eve_load_sdcard(emulator, displayError))
    {
        std::cerr << displayError << '\n';
        destroyEmulator(emulator, true);
        return 1;
    }
    std::cout << "Displaying frame 1 from bicycle.anim.reloc." << std::endl;

    while (BT8XXEMU_isRunning(emulator))
        Sleep(50);

    destroyEmulator(emulator, true);
    return 0;
}
