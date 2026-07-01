using System;
using System.Runtime.InteropServices;

namespace BT8XXEMU.Interop
{
	/// <summary>
	/// Raw P/Invoke declarations for BT8XXEMU library
	/// </summary>
	public static class BT8XXEMUNative
	{
		private const string DllName = "bt8xxemu";
		private const CallingConvention Convention = CallingConvention.Cdecl;

		public const uint BT8XXEMU_VERSION_API = 16;

		#region Enumerations

		public enum BT8XXEMU_LogType
		{
			BT8XXEMU_LogError = 0,
			BT8XXEMU_LogWarning = 1,
			BT8XXEMU_LogMessage = 2,
		}

		public enum BT8XXEMU_EmulatorMode
		{
			BT8XXEMU_EmulatorFT800 = 0x0800,
			BT8XXEMU_EmulatorFT801 = 0x0801,
			BT8XXEMU_EmulatorFT810 = 0x0810,
			BT8XXEMU_EmulatorFT811 = 0x0811,
			BT8XXEMU_EmulatorFT812 = 0x0812,
			BT8XXEMU_EmulatorFT813 = 0x0813,
			BT8XXEMU_EmulatorBT880 = 0x0880,
			BT8XXEMU_EmulatorBT881 = 0x0881,
			BT8XXEMU_EmulatorBT882 = 0x0882,
			BT8XXEMU_EmulatorBT883 = 0x0883,
			BT8XXEMU_EmulatorBT815 = 0x0815,
			BT8XXEMU_EmulatorBT816 = 0x0816,
			BT8XXEMU_EmulatorBT817 = 0x0817,
			BT8XXEMU_EmulatorBT818 = 0x0818,
			BT8XXEMU_EmulatorBT820 = 0x0820,
		}

		[Flags]
		public enum BT8XXEMU_EmulatorFlags
		{
			BT8XXEMU_EmulatorEnableKeyboard = 0x01,
			BT8XXEMU_EmulatorEnableAudio = 0x02,
			BT8XXEMU_EmulatorEnableCoprocessor = 0x04,
			BT8XXEMU_EmulatorEnableMouse = 0x08,
			BT8XXEMU_EmulatorEnableDebugShortkeys = 0x10,
			BT8XXEMU_EmulatorEnableGraphicsMultithread = 0x20,
			BT8XXEMU_EmulatorEnableDynamicDegrade = 0x40,
			BT8XXEMU_EmulatorEnableRegPwmDutyEmulation = 0x100,
			BT8XXEMU_EmulatorEnableTouchTransformation = 0x200,
			BT8XXEMU_EmulatorEnableStdOut = 0x400,
			BT8XXEMU_EmulatorEnableBackgroundPerformance = 0x800,
			BT8XXEMU_EmulatorEnableMainPerformance = 0x1000,
			BT8XXEMU_EmulatorEnableHSFPreview = 0x2000,
			BT8XXEMU_EmulatorEnableSDCardReadOnly = 0x4000,
			BT8XXEMU_EmulatorEnableRamGMaxReserve = 0x8000,
			BT8XXEMU_EmulatorEnableRamGMaxCommit = 0x10000,
			BT8XXEMU_EmulatorEnableFastForwardSwapchain = 0x20000,
		}

		[Flags]
		public enum BT8XXEMU_FrameFlags
		{
			BT8XXEMU_FrameBufferChanged = 0x01,
			BT8XXEMU_FrameBufferComplete = 0x02,
			BT8XXEMU_FrameChanged = 0x04,
			BT8XXEMU_FrameSwap = 0x08,
		}

		#endregion

		#region Delegates

		[UnmanagedFunctionPointer(Convention)]
		public delegate void MainCallback(IntPtr sender, IntPtr context);

		[UnmanagedFunctionPointer(Convention)]
		public delegate void MCUSleepCallback(IntPtr sender, IntPtr context, int ms);

		[UnmanagedFunctionPointer(Convention)]
		public delegate int GraphicsCallback(IntPtr sender, IntPtr context, int output, IntPtr buffer, uint hsize, uint vsize, BT8XXEMU_FrameFlags flags);

		[UnmanagedFunctionPointer(Convention)]
		public delegate void LogCallback(IntPtr sender, IntPtr context, BT8XXEMU_LogType type, IntPtr message);

		[UnmanagedFunctionPointer(Convention)]
		public delegate void CloseCallback(IntPtr sender, IntPtr context);

		[UnmanagedFunctionPointer(Convention)]
		public delegate void MCUWakeCallback(IntPtr sender, IntPtr context);

		[UnmanagedFunctionPointer(Convention)]
		public delegate void FlashLogCallback(IntPtr sender, IntPtr context, BT8XXEMU_LogType type, IntPtr message);

		#endregion

		#region Structures

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		public struct BT8XXEMU_EmulatorParameters
		{
			public MainCallback Main;
			public int Flags;
			public BT8XXEMU_EmulatorMode Mode;
			public uint MousePressure;
			public uint ExternalFrequency;
			public uint ReduceGraphicsThreads;
			public MCUSleepCallback MCUSleep;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string RomFilePath;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string OtpFilePath;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string CoprocessorRomFilePath;

			public GraphicsCallback Graphics;
			public LogCallback Log;
			public CloseCallback Close;
			public IntPtr UserContext;
			public IntPtr Flash;
			public IntPtr RamGSizeBytes;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string SDCardFilePath;

			public MCUWakeCallback MCUWake;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		public struct BT8XXEMU_FlashParameters
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 26)]
			public string DeviceType;

			public IntPtr SizeBytes;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string DataFilePath;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string StatusFilePath;

			public int Persistent;
			public int StdOut;
			public IntPtr Data;
			public IntPtr DataSizeBytes;
			public FlashLogCallback Log;
			public IntPtr UserContext;
		}

		#endregion

		#region Init Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern IntPtr BT8XXEMU_version();

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_defaults(uint versionApi, ref BT8XXEMU_EmulatorParameters parameters, BT8XXEMU_EmulatorMode mode);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_run(uint versionApi, ref IntPtr emulator, ref BT8XXEMU_EmulatorParameters parameters);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_stop(IntPtr emulator);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_destroy(IntPtr emulator);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern int BT8XXEMU_isRunning(IntPtr emulator);

		#endregion

		#region Runtime Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern byte BT8XXEMU_transfer(IntPtr emulator, byte data);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_chipSelect(IntPtr emulator, int cs);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern int BT8XXEMU_hasInterrupt(IntPtr emulator);

		#endregion

		#region Advanced Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_touchSetXY(IntPtr emulator, int idx, int x, int y, int pressure);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_touchResetXY(IntPtr emulator, int idx);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_touchTransformReset(IntPtr emulator);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern int BT8XXEMU_setFlag(IntPtr emulator, BT8XXEMU_EmulatorFlags flag, int value);

		#endregion

		#region SD Host Functions

		[DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Unicode)]
		public static extern int BT8XXEMU_insertSDCardImage(IntPtr emulator, string filePath, int readOnly);

		[DllImport(DllName, CallingConvention = Convention, CharSet = CharSet.Unicode)]
		public static extern IntPtr BT8XXEMU_insertSDCardFolder(IntPtr emulator, string folderPath, IntPtr minimumSize, int readOnly);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_ejectSDCard(IntPtr emulator);

		#endregion

		#region LVDSRX Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_setLvdsRXState(IntPtr emulator, int state, int width, int height, double freq);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern IntPtr BT8XXEMU_lvdsRXWp(IntPtr emulator, IntPtr size);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_advanceLvdsRXWp(IntPtr emulator);

		#endregion

		#region Flash Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_Flash_defaults(uint versionApi, ref BT8XXEMU_FlashParameters parameters);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern IntPtr BT8XXEMU_Flash_create(uint versionApi, ref BT8XXEMU_FlashParameters parameters);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_Flash_destroy(IntPtr flash);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern byte BT8XXEMU_Flash_transferSpi4(IntPtr flash, byte signal);

		#endregion

		#region SleepWake Functions

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern IntPtr BT8XXEMU_SleepWake_create();

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_SleepWake_destroy(IntPtr sleepWake);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_SleepWake_wake(IntPtr sleepWake);

		[DllImport(DllName, CallingConvention = Convention)]
		public static extern void BT8XXEMU_SleepWake_sleep(IntPtr sleepWake, int ms);

		#endregion
	}
}