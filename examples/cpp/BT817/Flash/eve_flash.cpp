#include "eve_flash.h"

#include <windows.h>

#include <cstdint>
#include <initializer_list>

namespace
{
constexpr uint32_t RAM_DL = 0x300000;
constexpr uint32_t RAM_CMD = 0x308000;
constexpr uint16_t RAM_CMD_MASK = 0x0FFF;

constexpr uint32_t REG_ID = 0x302000;
constexpr uint32_t REG_CPURESET = 0x302020;
constexpr uint32_t REG_HCYCLE = 0x30202C;
constexpr uint32_t REG_HOFFSET = 0x302030;
constexpr uint32_t REG_HSIZE = 0x302034;
constexpr uint32_t REG_HSYNC0 = 0x302038;
constexpr uint32_t REG_HSYNC1 = 0x30203C;
constexpr uint32_t REG_VCYCLE = 0x302040;
constexpr uint32_t REG_VOFFSET = 0x302044;
constexpr uint32_t REG_VSIZE = 0x302048;
constexpr uint32_t REG_VSYNC0 = 0x30204C;
constexpr uint32_t REG_VSYNC1 = 0x302050;
constexpr uint32_t REG_DLSWAP = 0x302054;
constexpr uint32_t REG_DITHER = 0x302060;
constexpr uint32_t REG_SWIZZLE = 0x302064;
constexpr uint32_t REG_CSPREAD = 0x302068;
constexpr uint32_t REG_PCLK_POL = 0x30206C;
constexpr uint32_t REG_PCLK = 0x302070;
constexpr uint32_t REG_GPIO = 0x302094;
constexpr uint32_t REG_PWM_DUTY = 0x3020D4;
constexpr uint32_t REG_CMD_READ = 0x3020F8;
constexpr uint32_t REG_CMD_WRITE = 0x3020FC;
constexpr uint32_t REG_FLASH_STATUS = 0x3025F0;

constexpr uint32_t CMD_DLSTART = 0xFFFFFF00;
constexpr uint32_t CMD_SWAP = 0xFFFFFF01;
constexpr uint32_t CMD_SETBITMAP = 0xFFFFFF43;
constexpr uint32_t CMD_FLASHDETACH = 0xFFFFFF48;
constexpr uint32_t CMD_FLASHATTACH = 0xFFFFFF49;
constexpr uint32_t CMD_FLASHFAST = 0xFFFFFF4A;

constexpr uint8_t FLASH_STATUS_DETACHED = 1;
constexpr uint8_t FLASH_STATUS_BASIC = 2;
constexpr uint8_t FLASH_STATUS_FULL = 3;

constexpr uint32_t DISPLAY = 0x00000000;
constexpr uint32_t CLEAR_COLOR_RGB_BLACK = 0x02000000;
constexpr uint32_t BEGIN_BITMAPS = 0x1F000001;
constexpr uint32_t END = 0x21000000;
constexpr uint32_t CLEAR_COLOR_STENCIL_TAG = 0x26000007;
constexpr uint32_t COMPRESSED_RGBA_ASTC_4x4_KHR = 0x000093B0;
constexpr uint32_t FLASH_BITMAP_SOURCE = 0x800000 | 128;
constexpr uint32_t BITMAP_WIDTH = 288;
constexpr uint32_t BITMAP_HEIGHT = 164;
constexpr DWORD OPERATION_TIMEOUT_MS = 3000;

uint32_t VERTEX2F(uint16_t x, uint16_t y)
{
    return 0x40000000U |
           ((static_cast<uint32_t>(x) & 0x7FFFU) << 15) |
           (static_cast<uint32_t>(y) & 0x7FFFU);
}

void beginTransfer(BT8XXEMU_Emulator *emulator, uint32_t address, bool write)
{
    BT8XXEMU_chipSelect(emulator, 1);
    const uint8_t operation = write ? 0x80 : 0x00;
    BT8XXEMU_transfer(
        emulator,
        static_cast<uint8_t>(operation | ((address >> 16) & 0x3F)));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(address >> 8));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(address));
}

void endTransfer(BT8XXEMU_Emulator *emulator)
{
    BT8XXEMU_chipSelect(emulator, 0);
}

void wr8(BT8XXEMU_Emulator *emulator, uint32_t address, uint8_t value)
{
    beginTransfer(emulator, address, true);
    BT8XXEMU_transfer(emulator, value);
    endTransfer(emulator);
}

void wr16(BT8XXEMU_Emulator *emulator, uint32_t address, uint16_t value)
{
    beginTransfer(emulator, address, true);
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value >> 8));
    endTransfer(emulator);
}

void wr32(BT8XXEMU_Emulator *emulator, uint32_t address, uint32_t value)
{
    beginTransfer(emulator, address, true);
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value >> 8));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value >> 16));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(value >> 24));
    endTransfer(emulator);
}

uint8_t rd8(BT8XXEMU_Emulator *emulator, uint32_t address)
{
    beginTransfer(emulator, address, false);
    BT8XXEMU_transfer(emulator, 0);
    const uint8_t value = BT8XXEMU_transfer(emulator, 0);
    endTransfer(emulator);
    return value;
}

uint16_t rd16(BT8XXEMU_Emulator *emulator, uint32_t address)
{
    beginTransfer(emulator, address, false);
    BT8XXEMU_transfer(emulator, 0);
    const uint16_t low = BT8XXEMU_transfer(emulator, 0);
    const uint16_t high = BT8XXEMU_transfer(emulator, 0);
    endTransfer(emulator);
    return static_cast<uint16_t>(low | (high << 8));
}

bool waitForBoot(BT8XXEMU_Emulator *emulator)
{
    const ULONGLONG deadline = GetTickCount64() + OPERATION_TIMEOUT_MS;
    while (GetTickCount64() < deadline && BT8XXEMU_isRunning(emulator))
    {
        if (rd8(emulator, REG_ID) == 0x7C &&
            rd8(emulator, REG_CPURESET) == 0)
            return true;
        Sleep(10);
    }
    return false;
}

bool waitForCommandFifo(BT8XXEMU_Emulator *emulator,
                        uint16_t expectedWritePointer)
{
    const ULONGLONG deadline = GetTickCount64() + OPERATION_TIMEOUT_MS;
    while (GetTickCount64() < deadline && BT8XXEMU_isRunning(emulator))
    {
        const uint16_t readPointer = rd16(emulator, REG_CMD_READ);
        if (readPointer == RAM_CMD_MASK)
            return false;
        if ((readPointer & RAM_CMD_MASK) == expectedWritePointer)
            return true;
        Sleep(1);
    }
    return false;
}

bool submitCommands(BT8XXEMU_Emulator *emulator,
                    std::initializer_list<uint32_t> commands)
{
    uint16_t writePointer =
        static_cast<uint16_t>(rd16(emulator, REG_CMD_WRITE) & RAM_CMD_MASK);
    if (!waitForCommandFifo(emulator, writePointer))
        return false;

    for (const uint32_t command : commands)
    {
        wr32(emulator, RAM_CMD + writePointer, command);
        writePointer = static_cast<uint16_t>((writePointer + 4) & RAM_CMD_MASK);
    }

    wr16(emulator, REG_CMD_WRITE, writePointer);
    return waitForCommandFifo(emulator, writePointer);
}

bool initializeDisplay(BT8XXEMU_Emulator *emulator)
{
    if (!waitForBoot(emulator))
        return false;

    wr8(emulator, REG_PCLK, 0);
    wr16(emulator, REG_HSIZE, 800);
    wr16(emulator, REG_HCYCLE, 928);
    wr16(emulator, REG_HOFFSET, 88);
    wr16(emulator, REG_HSYNC0, 0);
    wr16(emulator, REG_HSYNC1, 48);
    wr16(emulator, REG_VSIZE, 480);
    wr16(emulator, REG_VCYCLE, 525);
    wr16(emulator, REG_VOFFSET, 32);
    wr16(emulator, REG_VSYNC0, 0);
    wr16(emulator, REG_VSYNC1, 3);
    wr8(emulator, REG_PCLK_POL, 1);
    wr8(emulator, REG_SWIZZLE, 0);
    wr8(emulator, REG_CSPREAD, 0);
    wr8(emulator, REG_DITHER, 1);

    wr32(emulator, RAM_DL, CLEAR_COLOR_RGB_BLACK);
    wr32(emulator, RAM_DL + 4, CLEAR_COLOR_STENCIL_TAG);
    wr32(emulator, RAM_DL + 8, DISPLAY);
    wr8(emulator, REG_DLSWAP, 2);

    wr8(emulator, REG_GPIO,
        static_cast<uint8_t>(rd8(emulator, REG_GPIO) | 0x80));
    wr8(emulator, REG_PCLK, 2);
    wr8(emulator, REG_PWM_DUTY, 127);
    return true;
}

bool enableFlashFullMode(BT8XXEMU_Emulator *emulator)
{
    if (!submitCommands(emulator, {CMD_FLASHDETACH}) ||
        rd8(emulator, REG_FLASH_STATUS) != FLASH_STATUS_DETACHED)
        return false;

    if (!submitCommands(emulator, {CMD_FLASHATTACH}) ||
        rd8(emulator, REG_FLASH_STATUS) != FLASH_STATUS_BASIC)
        return false;

    return submitCommands(emulator, {CMD_FLASHFAST, 0}) &&
           rd8(emulator, REG_FLASH_STATUS) == FLASH_STATUS_FULL;
}
} // namespace

bool eve_load_flash(BT8XXEMU_Emulator *emulator, std::string &errorMessage)
{
    if (!emulator)
    {
        errorMessage = "The emulator is not initialized.";
        return false;
    }

    if (!initializeDisplay(emulator))
    {
        errorMessage = "The BT817 display did not initialize.";
        return false;
    }

    if (!enableFlashFullMode(emulator))
    {
        errorMessage = "The Flash device did not enter FULL mode.";
        return false;
    }

    const uint32_t bitmapFormatAndWidth =
        (BITMAP_WIDTH << 16) | COMPRESSED_RGBA_ASTC_4x4_KHR;
    if (!submitCommands(
            emulator,
            {CMD_DLSTART,
             CLEAR_COLOR_RGB_BLACK,
             CLEAR_COLOR_STENCIL_TAG,
             CMD_SETBITMAP,
             FLASH_BITMAP_SOURCE,
             bitmapFormatAndWidth,
             BITMAP_HEIGHT,
             BEGIN_BITMAPS,
             VERTEX2F(3000, 3000),
             END,
             DISPLAY,
             CMD_SWAP}))
    {
        errorMessage = "The Flash image display list did not complete.";
        return false;
    }

    errorMessage.clear();
    return true;
}
