using BT8XXEMU;
using BT8XXEMU.Interop;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BT8XXEMUCSharpDemo
{
  public partial class MainWindow : Form
  {
    private Emulator _emulator;
    private Thread _mcuThread;
    private volatile bool _running;
    private volatile bool _closing;
    private int _cmdBufferWp;
    private uint _lastTag;

    private volatile bool _mouseDown;
    private volatile uint _mouseX;
    private volatile uint _mouseY;
    private volatile bool _lastMouseDown;
    private volatile uint _lastMouseX;
    private volatile uint _lastMouseY;

    private Random r = new Random();

    // Flash image names enum for easy selection
    private enum FlashImageName
    {
      CarbonFiber,
      droHeroBarGraphArt,
      droHeroBarGraphSlider,
      droHeroBarGraphSliderL,
      droHeroBarGraphSliderR,
      droHeroLargeArt,
      droHeroSliderLarge,
      droHeroSmallArt,
      FullWhite,
      GaugeLinework,
      RPMPassed,
      RPMRedlineGrad
    }

    // Flash image information structure
    private struct FlashImageInfo
    {
      public string Name;
      public uint Address;
      public uint Width;
      public uint Height;
      public uint Format;
      public int OffsetX;
      public int OffsetY;

      public FlashImageInfo(string name, uint address, uint width, uint height, uint format = 37815, int offsetX = 0, int offsetY = 0)
      {
        Name = name;
        Address = address;
        Width = width;
        Height = height;
        Format = format; // Default to ASTC 8x8
        OffsetX = offsetX;
        OffsetY = offsetY;
      }
    }

    // Flash images array - arranged to display evenly on Carbon Fiber background (800x480)
    // Carbon Fiber serves as background, other images positioned on top
    private static readonly FlashImageInfo[] FlashImages = new FlashImageInfo[]
    {
			// Background layer - Carbon Fiber (800x480) centered
			new FlashImageInfo("Carbon Fiber_800x480_ASTC_8X8", 4096, 800, 480, 37815, 0, 0),
			
			// Top row - large images centered horizontally
			new FlashImageInfo("Gauge Linework_776x144_ASTC_8X8", 157312, 776, 144, 37815, 0, -160),
      new FlashImageInfo("Full white_752x128_ASTC_8X8", 133248, 752, 128, 37815, 0, -10),
			
			// Middle row - two medium images side by side
			new FlashImageInfo("droHeroLargeArt_336x120_ASTC_8X8", 109824, 336, 120, 37815, -220, 80),
      new FlashImageInfo("droHeroSliderLarge_336x120_ASTC_8X8", 119936, 336, 120, 37815, 220, 80),
			
			// Bottom row - three small images evenly spaced
			new FlashImageInfo("droHeroBarGraphArt_200x64_ASTC_8X8", 100096, 200, 64, 37815, -270, 180),
      new FlashImageInfo("droHeroBarGraphSlider_200x64_ASTC_8X8", 103296, 200, 64, 37815, 0, 180),
      new FlashImageInfo("droHeroSmallArt_200x64_ASTC_8X8", 130048, 200, 64, 37815, 270, 180),
			
			// Very small images at bottom corners and center
			new FlashImageInfo("droHeroBarGraphSliderL_104x64_ASTC_8X8", 106496, 104, 64, 37815, -300, 210),
      new FlashImageInfo("droHeroBarGraphSliderR_104x64_ASTC_8X8", 108160, 104, 64, 37815, 300, 210),
      new FlashImageInfo("RPM Redline Grad_128x24_ASTC_8X8", 186176, 128, 24, 37815, -180, 230),
      new FlashImageInfo("RPM Passed_48x72_ASTC_8X8", 185280, 48, 72, 37815, 180, 210)
    };

    // Helper method to get image info by name
    private static FlashImageInfo GetFlashImage(FlashImageName name)
    {
      return FlashImages[(int)name];
    }

    [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

    public MainWindow()
    {
      InitializeComponent();
    }

    private void launchButton_Click(object sender, EventArgs e)
    {
      if (_running)
        return;

      _running = true;
      launchButton.Enabled = false;
      stopButton.Enabled = true;

      _mcuThread = new Thread(MCUThread);
      _mcuThread.IsBackground = true;
      _mcuThread.Start();
    }

    private void stopButton_Click(object sender, EventArgs e)
    {
      _running = false;
    }

    private void MCUThread()
    {
      try
      {
        var parameters = EmulatorParameters.CreateDefault(BT8XXEMUNative.BT8XXEMU_EmulatorMode.BT8XXEMU_EmulatorBT817);

        // Set up graphics callback for offscreen rendering mode
        parameters.Graphics = OnGraphicsFrame;

        parameters.Log = (emulator, args) =>
        {
          // Log messages from emulator somewhere (should also check args.Type)
          Console.WriteLine($"[Emulator] {args.Message}");
        };

        FlashParameters flash_param = FlashParameters.CreateDefault();
        flash_param.DataFilePath = "flash-817-default.bin";
        flash_param.SizeBytes = 228480;
        parameters.Flash = Flash.Create(flash_param);

        _emulator = Emulator.Create(parameters);

        // Run setup (screen configuration, etc.)
        Setup();

        // Main loop (no need to throttle, emulator already throttles on CMD wait)
        while (_running && _emulator.IsRunning)
        {
          Loop();
        }

        if (_emulator.IsRunning)
        {
          // Stop here if exited by _running bool
          _emulator.Stop();
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Error: {ex.Message}", "Emulator Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
      finally
      {
        _emulator?.Dispose();
        _emulator = null;
        _running = false;

        // Re-enable button on UI thread
        if (!_closing)
        {
          this.Invoke((MethodInvoker)delegate
          {
            launchButton.Enabled = true;
            stopButton.Enabled = false;
          });
        }
      }
    }

    private void pictureBox_MouseMove(object sender, MouseEventArgs e)
    {
      _mouseX = (uint)e.X;
      _mouseY = (uint)e.Y;
      _mouseDown = (e.Button == MouseButtons.Left);
      if (_mouseDown)
      {
        // Ensure we pass at least one click
        _lastMouseX = _mouseX;
        _lastMouseY = _mouseY;
        _lastMouseDown = true;
      }
    }

    private void OnGraphicsFrame(Emulator sender, GraphicsEventArgs e)
    {
      if (!e.Output || e.Buffer == IntPtr.Zero)
        return;

      if (!_running || _closing)
        return;

      // Copy buffer to bitmap
      var bitmap = new Bitmap((int)e.Width, (int)e.Height, PixelFormat.Format32bppRgb);
      var bitmapData = bitmap.LockBits(
        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
        ImageLockMode.WriteOnly,
        PixelFormat.Format32bppRgb);

      try
      {
        uint bufferSize = e.Width * e.Height * 4;
        CopyMemory(bitmapData.Scan0, e.Buffer, bufferSize);
      }
      finally
      {
        bitmap.UnlockBits(bitmapData);
      }

      // Update UI on main thread (blocking invoke)
      try
      {
        this.Invoke((MethodInvoker)delegate
        {
          var oldBitmap = pictureBox.Image;
          pictureBox.Image = bitmap;
          oldBitmap?.Dispose();
        });
        if (_lastMouseDown)
        {
          _lastMouseDown = false;
          _emulator.SetTouchXY(0, (int)_lastMouseX, (int)_lastMouseY);
        }
        else if (_mouseDown)
        {
          _emulator.SetTouchXY(0, (int)_mouseX, (int)_mouseY);
        }
        else
        {
          _emulator.ResetTouchXY(0);
        }
      }
      catch (System.ObjectDisposedException)
      {
        _running = false;
      }
    }

    #region BT8XX Utility Functions

    private byte Rd8(uint address)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((address >> 16) & 0xFF));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));
      _emulator.Transfer(0x00);

      byte value = _emulator.Transfer(0);

      _emulator.ChipSelect(false);

      return value;
    }

    private ushort Rd16(uint address)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((address >> 16) & 0xFF));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));
      _emulator.Transfer(0x00);

      ushort value = _emulator.Transfer(0);
      value |= (ushort)(_emulator.Transfer(0) << 8);

      _emulator.ChipSelect(false);

      return value;
    }

    private uint Rd32(uint address)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((address >> 16) & 0xFF));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));
      _emulator.Transfer(0x00);

      uint value = _emulator.Transfer(0);
      value |= (uint)(_emulator.Transfer(0) << 8);
      value |= (uint)(_emulator.Transfer(0) << 16);
      value |= (uint)(_emulator.Transfer(0) << 24);

      _emulator.ChipSelect(false);

      return value;
    }

    private void Wr8(uint address, byte value)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((2 << 6) | ((address >> 16) & 0xFF)));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));

      _emulator.Transfer(value);

      _emulator.ChipSelect(false);
    }

    private void Wr16(uint address, ushort value)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((2 << 6) | ((address >> 16) & 0xFF)));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));

      _emulator.Transfer((byte)(value & 0xFF));
      _emulator.Transfer((byte)((value >> 8) & 0xFF));

      _emulator.ChipSelect(false);
    }

    private void Wr32(uint address, uint value)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((2 << 6) | ((address >> 16) & 0xFF)));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));

      _emulator.Transfer((byte)(value & 0xFF));
      _emulator.Transfer((byte)((value >> 8) & 0xFF));
      _emulator.Transfer((byte)((value >> 16) & 0xFF));
      _emulator.Transfer((byte)((value >> 24) & 0xFF));

      _emulator.ChipSelect(false);
    }

    private void WrStart(uint address)
    {
      _emulator.ChipSelect(true);

      _emulator.Transfer((byte)((2 << 6) | ((address >> 16) & 0xFF)));
      _emulator.Transfer((byte)((address >> 8) & 0xFF));
      _emulator.Transfer((byte)(address & 0xFF));
    }

    private void Wr8(byte value)
    {
      _emulator.Transfer((byte)(value & 0xFF));
    }

    private void Wr16(ushort value)
    {
      _emulator.Transfer((byte)(value & 0xFF));
      _emulator.Transfer((byte)((value >> 8) & 0xFF));
    }

    private void Wr32(uint value)
    {
      _emulator.Transfer((byte)(value & 0xFF));
      _emulator.Transfer((byte)((value >> 8) & 0xFF));
      _emulator.Transfer((byte)((value >> 16) & 0xFF));
      _emulator.Transfer((byte)((value >> 24) & 0xFF));
    }

    private int WrStr(string str)
    {
      int i = 0;
      foreach (char c in str)
      {
        _emulator.Transfer((byte)c);
        i++;
      }
      _emulator.Transfer(0);
      i++;

      int w = i;
      i %= 4;
      if (i != 0)
      {
        i = 4 - i;
        w += i;
        for (int j = 0; j < i; j++)
        {
          _emulator.Transfer(0);
        }
      }
      return w;
    }

    private void WrEnd()
    {
      _emulator.ChipSelect(false);
    }

    // CMD Buffer helper functions
    private void CmdStart()
    {
      _cmdBufferWp = (int)(Rd32(REG_CMD_WRITE) & 0xFFF);
      WrStart(RAM_CMD + (uint)_cmdBufferWp);
    }

    private void CmdWr32(uint value)
    {
      Wr32(value);
      _cmdBufferWp += 4;
    }

    private void CmdWr16(ushort value)
    {
      Wr16(value);
      _cmdBufferWp += 2;
    }

    private void CmdWr8(byte value)
    {
      Wr8(value);
      _cmdBufferWp += 1;
    }

    private void CmdWrStr(string str)
    {
      _cmdBufferWp += WrStr(str);
    }

    private void CmdEnd()
    {
      WrEnd();
      _cmdBufferWp &= 0xFFF;
      Wr32(REG_CMD_WRITE, (uint)_cmdBufferWp);
    }

    private void WaitCmdFifo()
    {
      int rp;
      do
      {
        rp = (int)(Rd32(REG_CMD_READ) & 0xFFF);
      } while (_cmdBufferWp != rp && _running);
    }

    private void CmdSetBitmap(uint source, uint format, uint width, uint height)
    {
      CmdWr32(CMD_SETBITMAP);
      CmdWr32(source);
      CmdWr16((ushort)format);
      CmdWr16((ushort)width);
      CmdWr16((ushort)height);
      CmdWr16(0); // padding
    }

    #endregion

    #region FT8XX Constants

    // Memory regions
    private const uint RAM_G = 0;
    private const uint RAM_DL = 3145728;
    private const uint RAM_REG = 3153920;
    private const uint RAM_CMD = 3178496;

    // Registers
    private const uint REG_ID = 3153920;
    private const uint REG_FRAMES = 3153924;
    private const uint REG_CLOCK = 3153928;
    private const uint REG_FREQUENCY = 3153932;
    private const uint REG_RENDERMODE = 3153936;
    private const uint REG_SNAPY = 3153940;
    private const uint REG_SNAPSHOT = 3153944;
    private const uint REG_SNAPFORMAT = 3153948;
    private const uint REG_CPURESET = 3153952;
    private const uint REG_TAP_CRC = 3153956;
    private const uint REG_TAP_MASK = 3153960;
    private const uint REG_HCYCLE = 3153964;
    private const uint REG_HOFFSET = 3153968;
    private const uint REG_HSIZE = 3153972;
    private const uint REG_HSYNC0 = 3153976;
    private const uint REG_HSYNC1 = 3153980;
    private const uint REG_VCYCLE = 3153984;
    private const uint REG_VOFFSET = 3153988;
    private const uint REG_VSIZE = 3153992;
    private const uint REG_VSYNC0 = 3153996;
    private const uint REG_VSYNC1 = 3154000;
    private const uint REG_DLSWAP = 3154004;
    private const uint REG_ROTATE = 3154008;
    private const uint REG_OUTBITS = 3154012;
    private const uint REG_DITHER = 3154016;
    private const uint REG_SWIZZLE = 3154020;
    private const uint REG_CSPREAD = 3154024;
    private const uint REG_PCLK_POL = 3154028;
    private const uint REG_PCLK = 3154032;
    private const uint REG_TAG_X = 3154036;
    private const uint REG_TAG_Y = 3154040;
    private const uint REG_TAG = 3154044;
    private const uint REG_VOL_PB = 3154048;
    private const uint REG_VOL_SOUND = 3154052;
    private const uint REG_SOUND = 3154056;
    private const uint REG_PLAY = 3154060;
    private const uint REG_GPIO_DIR = 3154064;
    private const uint REG_GPIO = 3154068;
    private const uint REG_GPIOX_DIR = 3154072;
    private const uint REG_GPIOX = 3154076;
    private const uint REG_INT_FLAGS = 3154088;
    private const uint REG_INT_EN = 3154092;
    private const uint REG_INT_MASK = 3154096;
    private const uint REG_PLAYBACK_START = 3154100;
    private const uint REG_PLAYBACK_LENGTH = 3154104;
    private const uint REG_PLAYBACK_READPTR = 3154108;
    private const uint REG_PLAYBACK_FREQ = 3154112;
    private const uint REG_PLAYBACK_FORMAT = 3154116;
    private const uint REG_PLAYBACK_LOOP = 3154120;
    private const uint REG_PLAYBACK_PLAY = 3154124;
    private const uint REG_PWM_HZ = 3154128;
    private const uint REG_PWM_DUTY = 3154132;
    private const uint REG_MACRO_0 = 3154136;
    private const uint REG_MACRO_1 = 3154140;
    private const uint REG_CMD_READ = 3154168;
    private const uint REG_CMD_WRITE = 3154172;
    private const uint REG_CMD_DL = 3154176;
    private const uint REG_TOUCH_MODE = 3154180;
    private const uint REG_CTOUCH_EXTENDED = 3154184;
    private const uint REG_TOUCH_ADC_MODE = 3154184;
    private const uint REG_TOUCH_CHARGE = 3154188;
    private const uint REG_TOUCH_SETTLE = 3154192;
    private const uint REG_TOUCH_OVERSAMPLE = 3154196;
    private const uint REG_TOUCH_RZTHRESH = 3154200;
    private const uint REG_TOUCH_RAW_XY = 3154204;
    private const uint REG_CTOUCH_TOUCH1_XY = 3154204;
    private const uint REG_TOUCH_RZ = 3154208;
    private const uint REG_CTOUCH_TOUCH4_Y = 3154208;
    private const uint REG_TOUCH_SCREEN_XY = 3154212;
    private const uint REG_CTOUCH_TOUCH0_XY = 3154212;
    private const uint REG_TOUCH_TAG_XY = 3154216;
    private const uint REG_TOUCH_TAG = 3154220;
    private const uint REG_TOUCH_TAG1_XY = 3154224;
    private const uint REG_TOUCH_TAG1 = 3154228;
    private const uint REG_TOUCH_TAG2_XY = 3154232;
    private const uint REG_TOUCH_TAG2 = 3154236;
    private const uint REG_TOUCH_TAG3_XY = 3154240;
    private const uint REG_TOUCH_TAG3 = 3154244;
    private const uint REG_TOUCH_TAG4_XY = 3154248;
    private const uint REG_TOUCH_TAG4 = 3154252;
    private const uint REG_TOUCH_TRANSFORM_A = 3154256;
    private const uint REG_TOUCH_TRANSFORM_B = 3154260;
    private const uint REG_TOUCH_TRANSFORM_C = 3154264;
    private const uint REG_TOUCH_TRANSFORM_D = 3154268;
    private const uint REG_TOUCH_TRANSFORM_E = 3154272;
    private const uint REG_TOUCH_TRANSFORM_F = 3154276;
    private const uint REG_CTOUCH_TOUCH4_X = 3154284;

    // Commands
    private const uint CMD_DLSTART = 4294967040;
    private const uint CMD_SWAP = 4294967041;
    private const uint CMD_INTERRUPT = 4294967042;
    private const uint CMD_CRC = 4294967043;
    private const uint CMD_HAMMERAUX = 4294967044;
    private const uint CMD_MARCH = 4294967045;
    private const uint CMD_EXECUTE = 4294967047;
    private const uint CMD_GETPOINT = 4294967048;
    private const uint CMD_BGCOLOR = 4294967049;
    private const uint CMD_FGCOLOR = 4294967050;
    private const uint CMD_GRADIENT = 4294967051;
    private const uint CMD_TEXT = 4294967052;
    private const uint CMD_BUTTON = 4294967053;
    private const uint CMD_KEYS = 4294967054;
    private const uint CMD_PROGRESS = 4294967055;
    private const uint CMD_SLIDER = 4294967056;
    private const uint CMD_SCROLLBAR = 4294967057;
    private const uint CMD_TOGGLE = 4294967058;
    private const uint CMD_GAUGE = 4294967059;
    private const uint CMD_CLOCK = 4294967060;
    private const uint CMD_CALIBRATE = 4294967061;
    private const uint CMD_SPINNER = 4294967062;
    private const uint CMD_STOP = 4294967063;
    private const uint CMD_MEMCRC = 4294967064;
    private const uint CMD_REGREAD = 4294967065;
    private const uint CMD_MEMWRITE = 4294967066;
    private const uint CMD_MEMSET = 4294967067;
    private const uint CMD_MEMZERO = 4294967068;
    private const uint CMD_MEMCPY = 4294967069;
    private const uint CMD_APPEND = 4294967070;
    private const uint CMD_SNAPSHOT = 4294967071;
    private const uint CMD_TOUCH_TRANSFORM = 4294967072;
    private const uint CMD_BITMAP_TRANSFORM = 4294967073;
    private const uint CMD_INFLATE = 4294967074;
    private const uint CMD_GETPTR = 4294967075;
    private const uint CMD_LOADIMAGE = 4294967076;
    private const uint CMD_GETPROPS = 4294967077;
    private const uint CMD_LOADIDENTITY = 4294967078;
    private const uint CMD_TRANSLATE = 4294967079;
    private const uint CMD_SCALE = 4294967080;
    private const uint CMD_ROTATE = 4294967081;
    private const uint CMD_SETMATRIX = 4294967082;
    private const uint CMD_SETFONT = 4294967083;
    private const uint CMD_TRACK = 4294967084;
    private const uint CMD_DIAL = 4294967085;
    private const uint CMD_NUMBER = 4294967086;
    private const uint CMD_SCREENSAVER = 4294967087;
    private const uint CMD_SKETCH = 4294967088;
    private const uint CMD_LOGO = 4294967089;
    private const uint CMD_COLDSTART = 4294967090;
    private const uint CMD_GETMATRIX = 4294967091;
    private const uint CMD_GRADCOLOR = 4294967092;
    private const uint CMD_SETBITMAP = 4294967107;
    private const uint CMD_FLASHERASE = 4294967108;
    private const uint CMD_FLASHWRITE = 4294967109;
    private const uint CMD_FLASHREAD = 4294967110;
    private const uint CMD_FLASHUPDATE = 4294967111;
    private const uint CMD_FLASHDETACH = 4294967112;
    private const uint CMD_FLASHATTACH = 4294967113;
    private const uint CMD_FLASHFAST = 4294967114;
    private const uint CMD_FLASHSPIDESEL = 4294967115;
    private const uint CMD_FLASHSPITX = 4294967116;
    private const uint CMD_FLASHSPIRX = 4294967117;
    private const uint CMD_FLASHSOURCE = 4294967118;
    private const uint CMD_CLEARCACHE = 4294967119;

    // Display list commands
    private static uint VERTEX2F(int x, int y) => (1 << 30) | (((uint)x & 32767) << 15) | (((uint)y & 32767) << 0);
    private static uint VERTEX2II(uint x, uint y, uint handle, uint cell) => ((uint)2 << 30) | ((x & 511) << 21) | ((y & 511) << 12) | ((handle & 31) << 7) | ((cell & 127) << 0);
    private static uint BITMAP_SOURCE(uint addr) => (1 << 24) | ((addr & 8388607) << 0);
    private static uint CLEAR_COLOR_RGB(byte red, byte green, byte blue) => (2 << 24) | ((uint)red << 16) | ((uint)green << 8) | ((uint)blue << 0);
    private static uint TAG(uint s) => (3 << 24) | ((s & 255) << 0);
    private static uint COLOR_RGB(byte red, byte green, byte blue) => (4 << 24) | ((uint)red << 16) | ((uint)green << 8) | ((uint)blue << 0);
    private static uint BITMAP_HANDLE(uint handle) => (5 << 24) | ((handle & 31) << 0);
    private static uint CELL(uint cell) => (6 << 24) | ((cell & 127) << 0);
    private static uint BITMAP_LAYOUT(uint format, uint linestride, uint height) => (7 << 24) | ((format & 31) << 19) | ((linestride & 1023) << 9) | ((height & 511) << 0);
    private static uint BITMAP_SIZE(uint filter, uint wrapx, uint wrapy, uint width, uint height) => (8 << 24) | ((filter & 1) << 20) | ((wrapx & 1) << 19) | ((wrapy & 1) << 18) | ((width & 511) << 9) | ((height & 511) << 0);
    private static uint ALPHA_FUNC(uint func, uint reference) => (9 << 24) | ((func & 7) << 8) | ((reference & 255) << 0);
    private static uint STENCIL_FUNC(uint func, uint reference, uint mask) => (10 << 24) | ((func & 7) << 16) | ((reference & 255) << 8) | ((mask & 255) << 0);
    private static uint BLEND_FUNC(uint src, uint dst) => (11 << 24) | ((src & 7) << 3) | ((dst & 7) << 0);
    private static uint STENCIL_OP(uint sfail, uint spass) => (12 << 24) | ((sfail & 7) << 3) | ((spass & 7) << 0);
    private static uint POINT_SIZE(uint size) => (13 << 24) | ((size & 8191) << 0);
    private static uint LINE_WIDTH(uint width) => (14 << 24) | ((width & 4095) << 0);
    private static uint CLEAR_COLOR_A(byte alpha) => (15 << 24) | ((uint)alpha << 0);
    private static uint COLOR_A(byte alpha) => (16 << 24) | ((uint)alpha << 0);
    private static uint CLEAR_STENCIL(uint s) => (17 << 24) | ((s & 255) << 0);
    private static uint CLEAR_TAG(uint s) => (18 << 24) | ((s & 255) << 0);
    private static uint STENCIL_MASK(uint mask) => (19 << 24) | ((mask & 255) << 0);
    private static uint TAG_MASK(uint mask) => (20 << 24) | ((mask & 1) << 0);
    private static uint BITMAP_TRANSFORM_A(uint a) => (21 << 24) | ((a & 131071) << 0);
    private static uint BITMAP_TRANSFORM_B(uint b) => (22 << 24) | ((b & 131071) << 0);
    private static uint BITMAP_TRANSFORM_C(uint c) => (23 << 24) | ((c & 16777215) << 0);
    private static uint BITMAP_TRANSFORM_D(uint d) => (24 << 24) | ((d & 131071) << 0);
    private static uint BITMAP_TRANSFORM_E(uint e) => (25 << 24) | ((e & 131071) << 0);
    private static uint BITMAP_TRANSFORM_F(uint f) => (26 << 24) | ((f & 16777215) << 0);
    private static uint SCISSOR_XY(uint x, uint y) => (27 << 24) | ((x & 2047) << 11) | ((y & 2047) << 0);
    private static uint SCISSOR_SIZE(uint width, uint height) => (28 << 24) | ((width & 4095) << 12) | ((height & 4095) << 0);
    private static uint CALL(uint dest) => (29 << 24) | ((dest & 65535) << 0);
    private static uint JUMP(uint dest) => (30 << 24) | ((dest & 65535) << 0);
    private static uint BEGIN(uint prim) => (31 << 24) | ((prim & 15) << 0);
    private static uint COLOR_MASK(uint r, uint g, uint b, uint a) => (32 << 24) | ((r & 1) << 3) | ((g & 1) << 2) | ((b & 1) << 1) | ((a & 1) << 0);
    private static uint END() => (33 << 24);
    private static uint SAVE_CONTEXT() => (34 << 24);
    private static uint RESTORE_CONTEXT() => (35 << 24);
    private static uint RETURN() => (36 << 24);
    private static uint MACRO(uint m) => (37 << 24) | ((m & 1) << 0);
    private static uint CLEAR(uint c, uint s, uint t) => (38 << 24) | ((c & 1) << 2) | ((s & 1) << 1) | ((t & 1) << 0);
    private static uint VERTEX_FORMAT(uint frac) => (39 << 24) | ((frac & 7) << 0);
    private static uint BITMAP_LAYOUT_H(uint linestride, uint height) => (40 << 24) | ((linestride & 3) << 2) | ((height & 3) << 0);
    private static uint BITMAP_SIZE_H(uint width, uint height) => (41 << 24) | ((width & 3) << 2) | ((height & 3) << 0);
    private static uint PALETTE_SOURCE(uint addr) => (42 << 24) | ((addr & 4194303) << 0);
    private static uint VERTEX_TRANSLATE_X(int x) => (43 << 24) | (((uint)x & 131071) << 0);
    private static uint VERTEX_TRANSLATE_Y(int y) => (44 << 24) | (((uint)y & 131071) << 0);
    private static uint BITMAP_EXT_FORMAT(uint format) => (46 << 24) | ((format & 65535) << 0);
    private static uint NOP() => (45 << 24);
    private static uint DISPLAY() => (0 << 24);

    // Primitives
    private const uint BITMAPS = 1;
    private const uint POINTS = 2;
    private const uint LINES = 3;
    private const uint LINE_STRIP = 4;
    private const uint EDGE_STRIP_R = 5;
    private const uint EDGE_STRIP_L = 6;
    private const uint EDGE_STRIP_A = 7;
    private const uint EDGE_STRIP_B = 8;
    private const uint RECTS = 9;

    // Options
    private const ushort OPT_MONO = 1;
    private const ushort OPT_NODL = 2;
    private const ushort OPT_FLAT = 256;
    private const ushort OPT_SIGNED = 256;
    private const ushort OPT_CENTERX = 512;
    private const ushort OPT_CENTERY = 1024;
    private const ushort OPT_CENTER = 1536;
    private const ushort OPT_RIGHTX = 2048;
    private const ushort OPT_NOBACK = 4096;
    private const ushort OPT_NOTICKS = 8192;
    private const ushort OPT_NOHM = 16384;
    private const ushort OPT_NOPOINTER = 16384;
    private const ushort OPT_NOSECS = 32768;
    private const ushort OPT_NOHANDS = 49152;
    private const ushort OPT_NOTEAR = 4;
    private const ushort OPT_FULLSCREEN = 8;
    private const ushort OPT_MEDIAFIFO = 16;
    private const ushort OPT_SOUND = 32;

    // Bitmap formats
    private const uint COMPRESSED_RGBA_ASTC_4x4_KHR = 37808;
    private const uint COMPRESSED_RGBA_ASTC_5x4_KHR = 37809;
    private const uint COMPRESSED_RGBA_ASTC_5x5_KHR = 37810;
    private const uint COMPRESSED_RGBA_ASTC_6x5_KHR = 37811;
    private const uint COMPRESSED_RGBA_ASTC_6x6_KHR = 37812;
    private const uint COMPRESSED_RGBA_ASTC_8x5_KHR = 37813;
    private const uint COMPRESSED_RGBA_ASTC_8x6_KHR = 37814;
    private const uint COMPRESSED_RGBA_ASTC_8x8_KHR = 37815;
    private const uint COMPRESSED_RGBA_ASTC_10x5_KHR = 37816;
    private const uint COMPRESSED_RGBA_ASTC_10x6_KHR = 37817;
    private const uint COMPRESSED_RGBA_ASTC_10x8_KHR = 37818;
    private const uint COMPRESSED_RGBA_ASTC_10x10_KHR = 37819;
    private const uint COMPRESSED_RGBA_ASTC_12x10_KHR = 37820;
    private const uint COMPRESSED_RGBA_ASTC_12x12_KHR = 37821;

    #endregion

    #region Setup and Loop

    private void Setup()
    {
      Wr32(REG_PCLK, 5);
      Wr32(REG_HSIZE, 1280);
      Wr32(REG_VSIZE, 720);
      Wr32(REG_HCYCLE, 1650);
      Wr32(REG_HOFFSET, 40 + 220);
      Wr32(REG_HSYNC0, 0);
      Wr32(REG_HSYNC1, 40);
      Wr32(REG_VCYCLE, 750);
      Wr32(REG_VOFFSET, 5 + 20);
      Wr32(REG_VSYNC0, 0);
      Wr32(REG_VSYNC1, 5);
      Wr32(REG_PCLK, 1);

      Wr32(REG_CSPREAD, 0);
      Wr32(REG_DITHER, 0);
      Wr32(REG_PCLK_POL, 0);
      Wr32(REG_OUTBITS, 0);
    }

    private void lcd_1280x720()
    {
      // Configure display for 1280x720 LCD
      Wr32(REG_HSIZE, 1280);
      Wr32(REG_VSIZE, 720);
      Wr32(REG_HCYCLE, 1650);
      Wr32(REG_HOFFSET, 40 + 220);
      Wr32(REG_HSYNC0, 0);
      Wr32(REG_HSYNC1, 40);
      Wr32(REG_VCYCLE, 750);
      Wr32(REG_VOFFSET, 5 + 20);
      Wr32(REG_VSYNC0, 0);
      Wr32(REG_VSYNC1, 5);
      Wr32(REG_PCLK, 1);

      Wr32(REG_CSPREAD, 0);
      Wr32(REG_DITHER, 0);
      Wr32(REG_PCLK_POL, 0);
      Wr32(REG_OUTBITS, 0); // OutBitsR, OutBitsG, OutBitsB all 0
    }

    private void Loop()
    {
      //Example1();
      //Example2_Display_1_Image();
      Example2_Display_All_Image();
    }

    private void Example2_Display_All_Image()
    {
      lcd_1280x720();

      // Initialize flash to fast mode (do this once at startup)
      CmdStart();
      CmdWr32(CMD_FLASHFAST);
      CmdWr32(0); // result pointer (0 = ignore)
      CmdEnd();
      WaitCmdFifo();

      // Start building display list
      CmdStart();
      CmdWr32(CMD_DLSTART);

      // Clear screen
      CmdWr32(CLEAR_COLOR_RGB(0, 0, 0));
      CmdWr32(CLEAR(1, 1, 1));

      // Draw all images from flash
      foreach (var imageInfo in FlashImages)
      {
        // Set flash as bitmap source
        CmdWr32(CMD_FLASHSOURCE);
        CmdWr32(imageInfo.Address);

        // Use CMD_SETBITMAP to configure bitmap in one command
        uint flashSource = 0x800000 | (imageInfo.Address / 32);
        CmdSetBitmap(flashSource, imageInfo.Format, imageInfo.Width, imageInfo.Height);

        // Draw the bitmap centered on screen (1280x720) with offset
        int x = (1280 - (int)imageInfo.Width) / 2 + imageInfo.OffsetX;
        int y = (720 - (int)imageInfo.Height) / 2 + imageInfo.OffsetY;
        CmdWr32(BEGIN(BITMAPS));
        CmdWr32(VERTEX2F(x * 16, y * 16));
        CmdWr32(END());
      }

      // Finish display list
      CmdWr32(DISPLAY());
      CmdWr32(CMD_SWAP);

      CmdEnd();
      WaitCmdFifo();
    }

    private void Example2_Display_1_Image()
    {
      lcd_1280x720();
      // do flash_fast
      // display image from flash using CMD_SETBITMAP
      // Change the imageName to select different images
      FlashImageName imageName = FlashImageName.droHeroBarGraphSliderR;

      // Get image info by name
      var imageInfo = GetFlashImage(imageName);

      // Initialize flash to fast mode (do this once at startup)
      CmdStart();
      CmdWr32(CMD_FLASHFAST);
      CmdWr32(0); // result pointer (0 = ignore)
      CmdEnd();
      WaitCmdFifo();

      // Start building display list
      CmdStart();
      CmdWr32(CMD_DLSTART);

      // Clear screen
      CmdWr32(CLEAR_COLOR_RGB(0, 0, 0));
      CmdWr32(CLEAR(1, 1, 1));

      // Set flash as bitmap source
      CmdWr32(CMD_FLASHSOURCE);
      CmdWr32(imageInfo.Address); // Flash address where the image is stored

      // Use CMD_SETBITMAP to configure bitmap in one command
      uint flashSource = 0x800000 | (imageInfo.Address / 32); // Flash memory address

      CmdSetBitmap(flashSource, imageInfo.Format, imageInfo.Width, imageInfo.Height);

      // Draw the bitmap centered on screen (1280x720)
      int centerX = (1280 - (int)imageInfo.Width) / 2;
      int centerY = (720 - (int)imageInfo.Height) / 2;
      CmdWr32(BEGIN(BITMAPS));
      CmdWr32(VERTEX2F(centerX * 16, centerY * 16));
      CmdWr32(END());

      // Add some text overlay
      CmdWr32(COLOR_RGB(255, 255, 255));
      CmdWr32(CMD_TEXT);
      CmdWr16(400); CmdWr16(20);
      CmdWr16(31); CmdWr16(OPT_CENTER);
      //CmdWrStr($"{imageInfo.Name}");

      // Finish display list
      CmdWr32(DISPLAY());
      CmdWr32(CMD_SWAP);

      CmdEnd();
      WaitCmdFifo();
    }

    private void Example1()
    {
      lcd_1280x720();

      int frame = (int)Rd32(REG_FRAMES);
      double time = frame / 60.0;

      // Read current touch tag
      byte touchTag = Rd8(REG_TOUCH_TAG);
      if (touchTag != _lastTag)
      {
        if (touchTag != 255)
        {
          if (_lastTag == 10)
          {
            // Invoke random window background color change
            try
            {
              this.Invoke((MethodInvoker)delegate
              {
                this.BackColor = Color.FromArgb(
                  128 + r.Next(128),
                  128 + r.Next(128),
                  128 + r.Next(128));
              });
            }
            catch (System.ObjectDisposedException)
            {
              _running = false;
            }
          }
          _lastTag = touchTag;
        }
      }

      // Touch coordinate debug output
      uint touchXY = Rd32(REG_TOUCH_SCREEN_XY);
      int touchX = (int)(touchXY >> 16);
      int touchY = (int)(touchXY & 0xFFFF);
      uint touchRawXY = Rd32(REG_TOUCH_RAW_XY);
      int touchRawX = (int)(touchRawXY >> 16);
      int touchRawY = (int)(touchRawXY & 0xFFFF);

      // Start building display list in CMD buffer
      CmdStart();

      // Start new display list
      CmdWr32(CMD_DLSTART);
      CmdWr32(TAG(255));

      // Animated gradient background
      byte r1 = (byte)(128 + 127 * Math.Sin(time * 0.3));
      byte g1 = (byte)(128 + 127 * Math.Sin(time * 0.4));
      byte b1 = (byte)(128 + 127 * Math.Sin(time * 0.5));

      byte r2 = (byte)(128 + 127 * Math.Cos(time * 0.3));
      byte g2 = (byte)(128 + 127 * Math.Cos(time * 0.4));
      byte b2 = (byte)(128 + 127 * Math.Cos(time * 0.5));

      CmdWr32(CMD_GRADIENT);
      CmdWr16(0); CmdWr16(0);
      CmdWr32((uint)(r1 << 16 | g1 << 8 | b1));
      CmdWr16(1280); CmdWr16(720);
      CmdWr32((uint)(r2 << 16 | g2 << 8 | b2));

      // Draw animated circles with points
      CmdWr32(BEGIN(POINTS));

      for (int i = 0; i < 8; i++)
      {
        double angle = (time + i * Math.PI / 4) * 0.8;
        int x = (int)(240 + 150 * Math.Cos(angle));
        int y = (int)(136 + 100 * Math.Sin(angle));

        byte r = (byte)(128 + 127 * Math.Sin(time + i));
        byte g = (byte)(128 + 127 * Math.Sin(time + i + 2));
        byte b = (byte)(128 + 127 * Math.Sin(time + i + 4));

        CmdWr32(COLOR_RGB(r, g, b));
        CmdWr32(COLOR_A((byte)(128 + 64 * Math.Sin(time * 2 + i))));
        CmdWr32(POINT_SIZE((uint)(300 + 200 * Math.Sin(time * 1.5 + i * 0.5))));
        CmdWr32(VERTEX2F(x * 16, y * 16));
      }

      CmdWr32(END());

      // Draw rotating star pattern with lines
      CmdWr32(COLOR_RGB(255, 255, 255));
      CmdWr32(COLOR_A(200));
      CmdWr32(LINE_WIDTH(32));
      CmdWr32(BEGIN(LINES));

      for (int i = 0; i < 12; i++)
      {
        double angle = time + i * Math.PI / 6;
        int x1 = (int)(240 + 80 * Math.Cos(angle));
        int y1 = (int)(136 + 80 * Math.Sin(angle));
        int x2 = (int)(240 + 40 * Math.Cos(angle));
        int y2 = (int)(136 + 40 * Math.Sin(angle));

        CmdWr32(VERTEX2F(x1 * 16, y1 * 16));
        CmdWr32(VERTEX2F(x2 * 16, y2 * 16));
      }

      CmdWr32(END());

      // Draw bouncing rectangles
      CmdWr32(BEGIN(RECTS));

      for (int i = 0; i < 5; i++)
      {
        double bounceY = 50 + 150 * (0.5 + 0.5 * Math.Sin(time * 2 + i * 0.7));
        int rectX = 50 + i * 80;

        byte rectR = (byte)(100 + 155 * (i / 5.0));
        byte rectG = (byte)(255 - 155 * (i / 5.0));
        byte rectB = (byte)(150);

        CmdWr32(COLOR_RGB(rectR, rectG, rectB));
        CmdWr32(COLOR_A(180));
        CmdWr32(VERTEX2F(rectX * 16, (int)bounceY * 16));
        CmdWr32(VERTEX2F((rectX + 50) * 16, ((int)bounceY + 60) * 16));
      }

      CmdWr32(END());

      // Draw animated text with shadow effect
      CmdWr32(COLOR_RGB(0, 0, 0));
      CmdWr32(COLOR_A(128));
      CmdWr32(CMD_TEXT);
      CmdWr16(242); CmdWr16(22);
      CmdWr16(31); CmdWr16(OPT_CENTER);
      CmdWrStr("BT8XX Emulator Demo");

      CmdWr32(COLOR_RGB(255, 255, 255));
      CmdWr32(COLOR_A(255));
      CmdWr32(CMD_TEXT);
      CmdWr16(240); CmdWr16(20);
      CmdWr16(31); CmdWr16(OPT_CENTER);
      CmdWrStr("BT8XX Emulator Demo");

      // Draw FPS counter
      CmdWr32(CMD_TEXT);
      CmdWr16(10); CmdWr16(252);
      CmdWr16(26); CmdWr16(0);
      CmdWrStr($"Frame: {frame}");

      // Draw button with tag
      CmdWr32(CMD_FGCOLOR);
      CmdWr32(touchTag == 10 ? (uint)0x00AA00 : 0x0000AA); // Green if pressed, blue otherwise
      CmdWr32(TAG(10));
      CmdWr32(CMD_BUTTON);
      CmdWr16(340); CmdWr16(200);  // x, y
      CmdWr16(120); CmdWr16(50);   // w, h
      CmdWr16(28);                  // font
      CmdWr16((ushort)(touchTag == 10 ? OPT_FLAT : 0)); // options - flat if pressed
      CmdWrStr("Click Me!");
      CmdWr32(TAG(255));

      // Draw touch status
      CmdWr32(COLOR_RGB(255, 255, 255));
      CmdWr32(CMD_TEXT);
      CmdWr16(10); CmdWr16(230);
      CmdWr16(26); CmdWr16(0);
      CmdWrStr($"Tag: {touchTag}");
      CmdWr32(CMD_TEXT);
      CmdWr16(10); CmdWr16(210);
      CmdWr16(26); CmdWr16(0);
      CmdWrStr($"Touch XY: ({touchX}, {touchY})");

      // Draw animated progress bar
      int progress = (int)((Math.Sin(time) * 0.5 + 0.5) * 65535);
      CmdWr32(CMD_BGCOLOR);
      CmdWr32(0x404040);
      CmdWr32(CMD_PROGRESS);
      CmdWr16(100); CmdWr16(180);  // x, y
      CmdWr16(280); CmdWr16(20);   // w, h
      CmdWr16(0);                   // options
      CmdWr16((ushort)progress);    // val
      CmdWr32(65535);               // range

      // Draw animated gauge
      int gaugeVal = (int)((Math.Sin(time * 1.5) * 0.5 + 0.5) * 100);
      CmdWr32(CMD_FGCOLOR);
      CmdWr32(0x00FF00);
      CmdWr32(CMD_GAUGE);
      CmdWr16(420); CmdWr16(80);
      CmdWr16(40); CmdWr16(0);
      CmdWr16(5); CmdWr16(10);
      CmdWr16((ushort)gaugeVal); CmdWr16(100);

      // Draw animated clock
      int hours = (int)(time / 5) % 12;
      int minutes = (int)((time % 5) * 12);
      int seconds = (int)(time * 60) % 60;

      CmdWr32(CMD_CLOCK);
      CmdWr16(60); CmdWr16(80);
      CmdWr16(40); CmdWr16(0);
      CmdWr16((ushort)hours); CmdWr16((ushort)minutes);
      CmdWr16((ushort)seconds); CmdWr16(0);

      // Finish display list
      CmdWr32(DISPLAY());
      CmdWr32(CMD_SWAP);

      CmdEnd();

      // Wait for command buffer to complete
      WaitCmdFifo();
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      _closing = true;
      _running = false;

      base.OnFormClosing(e);
    }
  }
}