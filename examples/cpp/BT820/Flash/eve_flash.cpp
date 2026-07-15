#include "eve_flash.h"

#include <windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <initializer_list>

namespace
{
constexpr uint16_t COMMAND_FIFO_MASK = 0x3FFFU;

constexpr uint32_t VERTEX2F(uint16_t x, uint16_t y)
{
    return 0x40000000U |
           ((static_cast<uint32_t>(x) & 0x7FFFU) << 15) |
           (static_cast<uint32_t>(y) & 0x7FFFU);
}

constexpr std::array<uint32_t, 12> flashReadBitmapCommands()
{
    constexpr uint32_t CMD_FLASHREAD = 0xFFFFFF40U;
    constexpr uint32_t CMD_SETBITMAP = 0xFFFFFF3DU;
    constexpr uint32_t BITMAP_HANDLE_0 = 0x05000000U;
    constexpr uint32_t BEGIN_BITMAPS = 0x1F000001U;
    constexpr uint32_t END = 0x21000000U;
    constexpr uint32_t FLASH_ADDRESS = 4096U;
    constexpr uint32_t BITMAP_BYTE_COUNT = 140832U;
    constexpr uint32_t BITMAP_WIDTH = 288U;
    constexpr uint32_t BITMAP_HEIGHT = 163U;
    constexpr uint32_t RGB8 = 19U;

    return {
        CMD_FLASHREAD,
        0,
        FLASH_ADDRESS,
        BITMAP_BYTE_COUNT,
        BITMAP_HANDLE_0,
        CMD_SETBITMAP,
        0,
        (BITMAP_WIDTH << 16) | RGB8,
        BITMAP_HEIGHT,
        BEGIN_BITMAPS,
        VERTEX2F(2592, 2528),
        END};
}

constexpr std::array<uint8_t, 4> addressHeader(uint32_t address, bool write)
{
    return {
        static_cast<uint8_t>(((address >> 24) & 0x7FU) |
                             (write ? 0x80U : 0x00U)),
        static_cast<uint8_t>(address >> 16),
        static_cast<uint8_t>(address >> 8),
        static_cast<uint8_t>(address)};
}

constexpr uint32_t RAM_CMD = 0x7F000000U;

constexpr uint32_t REG_ID = 0x7F006000U;
constexpr uint32_t REG_FREQUENCY = 0x7F00600CU;
constexpr uint32_t REG_RE_DEST = 0x7F006010U;
constexpr uint32_t REG_RE_FORMAT = 0x7F006014U;
constexpr uint32_t REG_RE_W = 0x7F00601CU;
constexpr uint32_t REG_RE_H = 0x7F006020U;
constexpr uint32_t REG_SC0_SIZE = 0x7F006038U;
constexpr uint32_t REG_SC0_PTR0 = 0x7F00603CU;
constexpr uint32_t REG_SC0_PTR1 = 0x7F006040U;
constexpr uint32_t REG_HCYCLE = 0x7F00608CU;
constexpr uint32_t REG_HOFFSET = 0x7F006090U;
constexpr uint32_t REG_HSIZE = 0x7F006094U;
constexpr uint32_t REG_HSYNC0 = 0x7F006098U;
constexpr uint32_t REG_HSYNC1 = 0x7F00609CU;
constexpr uint32_t REG_VCYCLE = 0x7F0060A0U;
constexpr uint32_t REG_VOFFSET = 0x7F0060A4U;
constexpr uint32_t REG_VSIZE = 0x7F0060A8U;
constexpr uint32_t REG_VSYNC0 = 0x7F0060ACU;
constexpr uint32_t REG_VSYNC1 = 0x7F0060B0U;
constexpr uint32_t REG_PCLK_POL = 0x7F0060B8U;
constexpr uint32_t REG_DISP = 0x7F0060E4U;
constexpr uint32_t REG_PWM_DUTY = 0x7F00612CU;
constexpr uint32_t REG_CMD_READ = 0x7F00614CU;
constexpr uint32_t REG_CMD_WRITE = 0x7F006150U;
constexpr uint32_t REG_FLASH_STATUS = 0x7F0065D4U;
constexpr uint32_t REG_SO_MODE = 0x7F0065F4U;
constexpr uint32_t REG_SO_SOURCE = 0x7F0065F8U;
constexpr uint32_t REG_SO_FORMAT = 0x7F0065FCU;
constexpr uint32_t REG_SO_EN = 0x7F006600U;
constexpr uint32_t REG_I2S_EN = 0x7F006714U;
constexpr uint32_t REG_I2S_FREQ = 0x7F006718U;
constexpr uint32_t LVDS_EN = 0x7F800300U;
constexpr uint32_t LVDSPLL_CFG = 0x7F800304U;
constexpr uint32_t I2S_CFG = 0x7F800800U;
constexpr uint32_t I2S_CTL = 0x7F800804U;

constexpr uint32_t CMD_DLSTART = 0xFFFFFF00U;
constexpr uint32_t CMD_SWAP = 0xFFFFFF01U;
constexpr uint32_t CMD_FLASHDETACH = 0xFFFFFF42U;
constexpr uint32_t CMD_FLASHATTACH = 0xFFFFFF43U;
constexpr uint32_t CMD_FLASHFAST = 0xFFFFFF44U;

constexpr uint8_t FLASH_STATUS_DETACHED = 1;
constexpr uint8_t FLASH_STATUS_BASIC = 2;
constexpr uint8_t FLASH_STATUS_FULL = 3;

constexpr uint32_t DISPLAY = 0x00000000U;
constexpr uint32_t CLEAR_COLOR_RGB_BLACK = 0x02000000U;
constexpr uint32_t CLEAR_COLOR_STENCIL_TAG = 0x26000007U;
constexpr uint32_t DISPLAY_WIDTH = 1280U;
constexpr uint32_t DISPLAY_HEIGHT = 720U;
constexpr uint32_t RGB8 = 19U;
constexpr uint32_t SWAPCHAIN_0 = 0xFFFF00FFU;
constexpr DWORD OPERATION_TIMEOUT_MS = 3000;
constexpr unsigned READ_READY_TRANSFER_LIMIT = 100000;

void beginTransfer(BT8XXEMU_Emulator *emulator, uint32_t address, bool write)
{
    BT8XXEMU_chipSelect(emulator, 1);
    for (const uint8_t value : addressHeader(address, write))
        BT8XXEMU_transfer(emulator, value);
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
    BT8XXEMU_transfer(emulator, 0);
    BT8XXEMU_transfer(emulator, 0);
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

bool waitForReadReady(BT8XXEMU_Emulator *emulator)
{
    for (unsigned attempt = 0; attempt < READ_READY_TRANSFER_LIMIT; ++attempt)
    {
        if (BT8XXEMU_transfer(emulator, 0) != 0)
            return true;
    }
    return false;
}

bool rd8(BT8XXEMU_Emulator *emulator, uint32_t address, uint8_t &value)
{
    beginTransfer(emulator, address, false);
    if (!waitForReadReady(emulator))
    {
        endTransfer(emulator);
        return false;
    }
    value = BT8XXEMU_transfer(emulator, 0);
    endTransfer(emulator);
    return true;
}

bool rd16(BT8XXEMU_Emulator *emulator, uint32_t address, uint16_t &value)
{
    beginTransfer(emulator, address, false);
    if (!waitForReadReady(emulator))
    {
        endTransfer(emulator);
        return false;
    }
    const uint16_t low = BT8XXEMU_transfer(emulator, 0);
    const uint16_t high = BT8XXEMU_transfer(emulator, 0);
    value = static_cast<uint16_t>(low | (high << 8));
    endTransfer(emulator);
    return true;
}

bool rd32(BT8XXEMU_Emulator *emulator, uint32_t address, uint32_t &value)
{
    beginTransfer(emulator, address, false);
    if (!waitForReadReady(emulator))
    {
        endTransfer(emulator);
        return false;
    }
    value = BT8XXEMU_transfer(emulator, 0);
    value |= static_cast<uint32_t>(BT8XXEMU_transfer(emulator, 0)) << 8;
    value |= static_cast<uint32_t>(BT8XXEMU_transfer(emulator, 0)) << 16;
    value |= static_cast<uint32_t>(BT8XXEMU_transfer(emulator, 0)) << 24;
    endTransfer(emulator);
    return true;
}

bool waitForBoot(BT8XXEMU_Emulator *emulator)
{
    const ULONGLONG deadline = GetTickCount64() + OPERATION_TIMEOUT_MS;
    while (GetTickCount64() < deadline && BT8XXEMU_isRunning(emulator))
    {
        uint32_t id = 0;
        if (rd32(emulator, REG_ID, id) && id == 0x7C)
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
        uint32_t readPointer = 0;
        if (!rd32(emulator, REG_CMD_READ, readPointer))
            return false;
        readPointer &= COMMAND_FIFO_MASK;
        if (readPointer == COMMAND_FIFO_MASK)
            return false;
        if (readPointer == expectedWritePointer)
            return true;
        Sleep(1);
    }
    return false;
}

bool submitCommands(BT8XXEMU_Emulator *emulator,
                    const uint32_t *commands,
                    size_t commandCount)
{
    uint32_t currentWritePointer = 0;
    if (!rd32(emulator, REG_CMD_WRITE, currentWritePointer))
        return false;

    uint16_t writePointer = static_cast<uint16_t>(
        currentWritePointer & COMMAND_FIFO_MASK);
    if (!waitForCommandFifo(emulator, writePointer))
        return false;

    for (size_t index = 0; index < commandCount; ++index)
    {
        wr32(emulator, RAM_CMD + writePointer, commands[index]);
        writePointer = static_cast<uint16_t>(
            (writePointer + 4) & COMMAND_FIFO_MASK);
    }

    wr32(emulator, REG_CMD_WRITE, writePointer);
    return waitForCommandFifo(emulator, writePointer);
}

bool submitCommands(BT8XXEMU_Emulator *emulator,
                    std::initializer_list<uint32_t> commands)
{
    return submitCommands(emulator, commands.begin(), commands.size());
}

template <size_t CommandCount>
bool submitCommands(BT8XXEMU_Emulator *emulator,
                    const std::array<uint32_t, CommandCount> &commands)
{
    return submitCommands(emulator, commands.data(), commands.size());
}

bool initializeDisplay(BT8XXEMU_Emulator *emulator)
{
    if (!waitForBoot(emulator))
        return false;

    wr32(emulator, REG_FREQUENCY, 72000000U);
    wr32(emulator, REG_SO_MODE, 3);
    wr32(emulator, REG_SO_EN, 1);
    wr32(emulator, REG_DISP, 1);
    wr32(emulator, REG_PWM_DUTY, 128);

    wr32(emulator, LVDSPLL_CFG, 0x00300870U);
    wr32(emulator, LVDS_EN, 0);
    wr32(emulator, LVDS_EN, 7);
    wr32(emulator, I2S_CTL, 0);
    wr32(emulator, I2S_CFG, 0x400U);
    wr32(emulator, REG_I2S_EN, 1);
    wr32(emulator, REG_I2S_FREQ, 0x3CF0U);

    wr16(emulator, REG_HSIZE, 1280);
    wr16(emulator, REG_VSIZE, 720);
    wr16(emulator, REG_HCYCLE, 960);
    wr16(emulator, REG_HOFFSET, 65296);
    wr16(emulator, REG_HSYNC0, 0);
    wr16(emulator, REG_HSYNC1, 65456);
    wr16(emulator, REG_VCYCLE, 625);
    wr16(emulator, REG_VOFFSET, 65464);
    wr16(emulator, REG_VSYNC0, 0);
    wr16(emulator, REG_VSYNC1, 65512);

    wr32(emulator, REG_SC0_SIZE, 2);
    wr32(emulator, REG_SC0_PTR0, 0x07ADD000U);
    wr32(emulator, REG_SC0_PTR1, 0x0783A000U);
    wr32(emulator, REG_RE_DEST, SWAPCHAIN_0);
    wr32(emulator, REG_RE_FORMAT, RGB8);
    wr32(emulator, REG_RE_W, DISPLAY_WIDTH);
    wr32(emulator, REG_RE_H, DISPLAY_HEIGHT);
    wr32(emulator, REG_SO_SOURCE, SWAPCHAIN_0);
    wr32(emulator, REG_SO_FORMAT, RGB8);
    wr8(emulator, REG_PCLK_POL, 0);

    uint16_t configuredWidth = 0;
    uint16_t configuredHeight = 0;
    return rd16(emulator, REG_HSIZE, configuredWidth) &&
           rd16(emulator, REG_VSIZE, configuredHeight) &&
           configuredWidth == DISPLAY_WIDTH &&
           configuredHeight == DISPLAY_HEIGHT;
}

bool enableFlashFullMode(BT8XXEMU_Emulator *emulator)
{
    uint8_t flashStatus = 0;
    if (!submitCommands(emulator, {CMD_FLASHDETACH}) ||
        !rd8(emulator, REG_FLASH_STATUS, flashStatus) ||
        flashStatus != FLASH_STATUS_DETACHED)
        return false;

    if (!submitCommands(emulator, {CMD_FLASHATTACH}) ||
        !rd8(emulator, REG_FLASH_STATUS, flashStatus) ||
        flashStatus != FLASH_STATUS_BASIC)
        return false;

    return submitCommands(emulator, {CMD_FLASHFAST, 0}) &&
           rd8(emulator, REG_FLASH_STATUS, flashStatus) &&
           flashStatus == FLASH_STATUS_FULL;
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
        errorMessage = "The BT820 display did not initialize.";
        return false;
    }

    if (!enableFlashFullMode(emulator))
    {
        errorMessage = "The Flash device did not enter FULL mode.";
        return false;
    }

    const auto imageCommands = flashReadBitmapCommands();
    if (!submitCommands(emulator,
                        {CMD_DLSTART,
                         CLEAR_COLOR_RGB_BLACK,
                         CLEAR_COLOR_STENCIL_TAG}) ||
        !submitCommands(emulator, imageCommands) ||
        !submitCommands(emulator, {DISPLAY, CMD_SWAP}))
    {
        errorMessage = "The Flash image display list did not complete.";
        return false;
    }

    errorMessage.clear();
    return true;
}
