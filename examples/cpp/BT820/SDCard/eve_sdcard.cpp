#include "eve_sdcard.h"

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <vector>

namespace
{
constexpr uint32_t RAM_CMD = 0x7F000000U;
constexpr uint16_t COMMAND_FIFO_MASK = 0x3FFFU;

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
constexpr uint32_t CMD_ANIMFRAME = 0xFFFFFF5EU;
constexpr uint32_t CMD_SDATTACH = 0xFFFFFF6EU;
constexpr uint32_t CMD_FSSOURCE = 0xFFFFFF7FU;
constexpr uint32_t CMD_LOADASSET = 0xFFFFFF81U;

constexpr uint32_t DISPLAY = 0x00000000U;
constexpr uint32_t CLEAR_COLOR_STENCIL_TAG = 0x26000007U;
constexpr uint32_t OPT_4BIT = 0x00000002U;
constexpr uint32_t OPT_IS_SD = 0x00000020U;
constexpr uint32_t OPT_FS = 0x00002000U;
constexpr uint32_t DISPLAY_WIDTH = 1280U;
constexpr uint32_t DISPLAY_HEIGHT = 720U;
constexpr uint32_t RGB8 = 19U;
constexpr uint32_t SWAPCHAIN_0 = 0xFFFF00FFU;
constexpr DWORD OPERATION_TIMEOUT_MS = 5000;
constexpr unsigned READ_READY_TRANSFER_LIMIT = 100000;

void beginTransfer(BT8XXEMU_Emulator *emulator, uint32_t address, bool write)
{
    BT8XXEMU_chipSelect(emulator, 1);
    BT8XXEMU_transfer(
        emulator,
        static_cast<uint8_t>(((address >> 24) & 0x7FU) |
                             (write ? 0x80U : 0x00U)));
    BT8XXEMU_transfer(emulator, static_cast<uint8_t>(address >> 16));
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
                    size_t commandCount,
                    uint16_t *commandStart = nullptr)
{
    uint32_t currentWritePointer = 0;
    if (!rd32(emulator, REG_CMD_WRITE, currentWritePointer))
        return false;

    uint16_t writePointer = static_cast<uint16_t>(
        currentWritePointer & COMMAND_FIFO_MASK);
    if (!waitForCommandFifo(emulator, writePointer))
        return false;

    if (commandStart)
        *commandStart = writePointer;
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

bool submitCommands(BT8XXEMU_Emulator *emulator,
                    const std::vector<uint32_t> &commands)
{
    return submitCommands(emulator, commands.data(), commands.size());
}

void appendString(std::vector<uint32_t> &commands, const char *text)
{
    uint32_t word = 0;
    unsigned byteIndex = 0;
    size_t textIndex = 0;
    uint8_t value = 0;
    do
    {
        value = static_cast<uint8_t>(text[textIndex++]);
        word |= static_cast<uint32_t>(value) << (byteIndex * 8);
        ++byteIndex;
        if (byteIndex == 4)
        {
            commands.push_back(word);
            word = 0;
            byteIndex = 0;
        }
    } while (value != 0);

    if (byteIndex != 0)
        commands.push_back(word);
}

bool submitCommandWithResult(BT8XXEMU_Emulator *emulator,
                             const std::vector<uint32_t> &commands,
                             size_t resultWordIndex,
                             uint32_t &result)
{
    if (resultWordIndex >= commands.size())
        return false;

    uint16_t commandStart = 0;
    if (!submitCommands(emulator,
                        commands.data(),
                        commands.size(),
                        &commandStart))
        return false;

    const uint16_t resultOffset = static_cast<uint16_t>(
        (commandStart + resultWordIndex * 4) & COMMAND_FIFO_MASK);
    return rd32(emulator, RAM_CMD + resultOffset, result);
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
    uint8_t configuredPclkPol = 0xFF;
    return rd16(emulator, REG_HSIZE, configuredWidth) &&
           rd16(emulator, REG_VSIZE, configuredHeight) &&
           rd8(emulator, REG_PCLK_POL, configuredPclkPol) &&
           configuredWidth == DISPLAY_WIDTH &&
           configuredHeight == DISPLAY_HEIGHT &&
           configuredPclkPol == 0;
}
} // namespace

bool eve_load_sdcard(BT8XXEMU_Emulator *emulator, std::string &errorMessage)
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

    if (!submitCommands(emulator, {CMD_DLSTART, CLEAR_COLOR_STENCIL_TAG}))
    {
        errorMessage = "The display list did not start.";
        return false;
    }

    uint32_t commandResult = 0xFFFFFFFFU;
    const std::vector<uint32_t> attachCommands = {
        CMD_SDATTACH, OPT_4BIT | OPT_IS_SD, 0};
    if (!submitCommandWithResult(
            emulator, attachCommands, 2, commandResult) ||
        commandResult != 0)
    {
        errorMessage = "CMD_SDATTACH failed with result " +
                       std::to_string(commandResult) + ".";
        return false;
    }

    std::vector<uint32_t> sourceCommands = {CMD_FSSOURCE};
    appendString(sourceCommands, "bicycle.anim.reloc");
    const size_t sourceResultIndex = sourceCommands.size();
    sourceCommands.push_back(0);
    commandResult = 0xFFFFFFFFU;

    if (!submitCommandWithResult(
            emulator, sourceCommands, sourceResultIndex, commandResult) ||
        commandResult != 0)
    {
        errorMessage = "CMD_FSSOURCE failed with result " +
                       std::to_string(commandResult) + ".";
        return false;
    }

    std::vector<uint32_t> loadCommands = {CMD_LOADASSET, 0, OPT_FS};
    if (!submitCommands(emulator, loadCommands))
    {
        errorMessage = "CMD_LOADASSET did not complete.";
        return false;
    }

    const uint32_t animationPosition =
        (static_cast<uint32_t>(300U) << 16) | 400U;
    if (!submitCommands(
            emulator,
            {CMD_ANIMFRAME,
             animationPosition,
             0,
             1,
             DISPLAY,
             CMD_SWAP}))
    {
        errorMessage = "CMD_ANIMFRAME did not complete.";
        return false;
    }

    errorMessage.clear();
    return true;
}
