using System;
using System.Runtime.InteropServices;
using BT8XXEMU.Interop;

namespace BT8XXEMU
{
	/// <summary>
	/// BT8XXEMU Emulator wrapper
	/// </summary>
	public class Emulator : IDisposable
	{
		private IntPtr _handle;
		private bool _disposed;
		private BT8XXEMUNative.BT8XXEMU_EmulatorParameters _parameters;

		// Keep delegates alive to prevent GC
		private BT8XXEMUNative.MCUSleepCallback _mcuSleepCallback;
		private BT8XXEMUNative.GraphicsCallback _graphicsCallback;
		private BT8XXEMUNative.LogCallback _logCallback;
		private BT8XXEMUNative.CloseCallback _closeCallback;
		private BT8XXEMUNative.MCUWakeCallback _mcuWakeCallback;

		public IntPtr Handle => _handle;

		public bool IsRunning => !_disposed && _handle != IntPtr.Zero && BT8XXEMUNative.BT8XXEMU_isRunning(_handle) != 0;

		/// <summary>
		/// Event raised when the emulator logs a message
		/// </summary>
		public event EventHandler<LogEventArgs> Log;

		/// <summary>
		/// Event raised when the emulator window is closed
		/// </summary>
		public event EventHandler Closed;

		/// <summary>
		/// Event raised for graphics output in driverless mode
		/// </summary>
		public event EventHandler<GraphicsEventArgs> Graphics;

		private Emulator()
		{
		}

		/// <summary>
		/// Create and run an emulator with the specified parameters
		/// </summary>
		public static Emulator Create(EmulatorParameters parameters)
		{
			if (parameters == null)
				throw new ArgumentNullException(nameof(parameters));

			var emulator = new Emulator();
			emulator.Initialize(parameters);
			return emulator;
		}

		/// <summary>
		/// Create an emulator with default parameters for the specified mode
		/// </summary>
		public static Emulator Create(BT8XXEMUNative.BT8XXEMU_EmulatorMode mode)
		{
			var parameters = EmulatorParameters.CreateDefault(mode);
			return Create(parameters);
		}

		private void Initialize(EmulatorParameters parameters)
		{
			// Start with defaults
			_parameters = new BT8XXEMUNative.BT8XXEMU_EmulatorParameters();
			BT8XXEMUNative.BT8XXEMU_defaults(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref _parameters, parameters.Mode);

			// Apply user settings
			_parameters.Flags = (int)parameters.Flags;
			_parameters.MousePressure = parameters.MousePressure;
			_parameters.ExternalFrequency = parameters.ExternalFrequency;
			_parameters.ReduceGraphicsThreads = parameters.ReduceGraphicsThreads;
			_parameters.RamGSizeBytes = new IntPtr(parameters.RamGSizeBytes);
			_parameters.RomFilePath = parameters.RomFilePath ?? string.Empty;
			_parameters.OtpFilePath = parameters.OtpFilePath ?? string.Empty;
			_parameters.CoprocessorRomFilePath = parameters.CoprocessorRomFilePath ?? string.Empty;
			_parameters.SDCardFilePath = parameters.SDCardFilePath ?? string.Empty;

			Console.WriteLine(parameters.Flash == null);
			Console.WriteLine(parameters.Flash?.Handle);

			_parameters.Flash = parameters.Flash?.Handle ?? IntPtr.Zero;
			_parameters.UserContext = IntPtr.Zero;
			_parameters.Main = null; // Deprecated

			// Setup callbacks
			if (parameters.MCUSleep != null)
			{
				_mcuSleepCallback = (sender, context, ms) => parameters.MCUSleep(this, ms);
				_parameters.MCUSleep = _mcuSleepCallback;
			}

			if (parameters.Graphics != null)
			{
				_graphicsCallback = (sender, context, output, buffer, hsize, vsize, flags) =>
				{
					var args = new GraphicsEventArgs(output != 0, buffer, hsize, vsize, flags);
					parameters.Graphics(this, args);
					return args.ContinueRunning ? 1 : 0;
				};
				_parameters.Graphics = _graphicsCallback;
			}
			else if (Graphics != null)
			{
				_graphicsCallback = (sender, context, output, buffer, hsize, vsize, flags) =>
				{
					var args = new GraphicsEventArgs(output != 0, buffer, hsize, vsize, flags);
					Graphics?.Invoke(this, args);
					return args.ContinueRunning ? 1 : 0;
				};
				_parameters.Graphics = _graphicsCallback;
			}

			if (parameters.Log != null || Log != null)
			{
				_logCallback = (sender, context, type, message) =>
				{
					var messageStr = Marshal.PtrToStringAnsi(message);
					var args = new LogEventArgs(type, messageStr);
					parameters.Log?.Invoke(this, args);
					Log?.Invoke(this, args);
				};
				_parameters.Log = _logCallback;
			}

			if (parameters.Close != null || Closed != null)
			{
				_closeCallback = (sender, context) =>
				{
					parameters.Close?.Invoke(this, EventArgs.Empty);
					Closed?.Invoke(this, EventArgs.Empty);
				};
				_parameters.Close = _closeCallback;
			}

			if (parameters.MCUWake != null)
			{
				_mcuWakeCallback = (sender, context) => parameters.MCUWake(this);
				_parameters.MCUWake = _mcuWakeCallback;
			}

			// Run the emulator
			BT8XXEMUNative.BT8XXEMU_run(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref _handle, ref _parameters);
		}

		/// <summary>
		/// Get the library version string
		/// </summary>
		public static string GetVersion()
		{
			var ptr = BT8XXEMUNative.BT8XXEMU_version();
			return Marshal.PtrToStringAnsi(ptr);
		}

		/// <summary>
		/// Transfer a byte over the SPI bus
		/// </summary>
		public byte Transfer(byte data)
		{
			ThrowIfDisposed();
			return BT8XXEMUNative.BT8XXEMU_transfer(_handle, data);
		}

		/// <summary>
		/// Set chip select (1 to start transfer, 0 to end)
		/// </summary>
		public void ChipSelect(bool select)
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_chipSelect(_handle, select ? 1 : 0);
		}

		/// <summary>
		/// Check if there is an interrupt flag set
		/// </summary>
		public bool HasInterrupt
		{
			get
			{
				ThrowIfDisposed();
				return BT8XXEMUNative.BT8XXEMU_hasInterrupt(_handle) != 0;
			}
		}

		/// <summary>
		/// Set touch position for a specific touch index (0-4)
		/// </summary>
		public void SetTouchXY(int index, int x, int y, int pressure = 0)
		{
			ThrowIfDisposed();
			if (index < 0 || index > 4)
				throw new ArgumentOutOfRangeException(nameof(index), "Touch index must be between 0 and 4");

			BT8XXEMUNative.BT8XXEMU_touchSetXY(_handle, index, x, y, pressure);
		}

		/// <summary>
		/// Reset touch position for a specific touch index
		/// </summary>
		public void ResetTouchXY(int index)
		{
			ThrowIfDisposed();
			if (index < 0 || index > 4)
				throw new ArgumentOutOfRangeException(nameof(index), "Touch index must be between 0 and 4");

			BT8XXEMUNative.BT8XXEMU_touchResetXY(_handle, index);
		}

		/// <summary>
		/// Reset the touch transform matrix to default
		/// </summary>
		public void ResetTouchTransform()
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_touchTransformReset(_handle);
		}

		/// <summary>
		/// Set a specific emulator flag on or off (only PWM and HSF options can be changed at runtime)
		/// </summary>
		public bool SetFlag(BT8XXEMUNative.BT8XXEMU_EmulatorFlags flag, bool value)
		{
			ThrowIfDisposed();
			return BT8XXEMUNative.BT8XXEMU_setFlag(_handle, flag, value ? 1 : 0) != 0;
		}

		/// <summary>
		/// Insert an SD card image file (BT820 and up only)
		/// </summary>
		public bool InsertSDCardImage(string filePath, bool readOnly = true)
		{
			ThrowIfDisposed();
			if (string.IsNullOrEmpty(filePath))
				throw new ArgumentNullException(nameof(filePath));

			return BT8XXEMUNative.BT8XXEMU_insertSDCardImage(_handle, filePath, readOnly ? 1 : 0) != 0;
		}

		/// <summary>
		/// Mount a folder as a virtual SD card (BT820 and up only)
		/// </summary>
		public long InsertSDCardFolder(string folderPath, long minimumSize = 0, bool readOnly = true)
		{
			ThrowIfDisposed();
			if (string.IsNullOrEmpty(folderPath))
				throw new ArgumentNullException(nameof(folderPath));

			var result = BT8XXEMUNative.BT8XXEMU_insertSDCardFolder(_handle, folderPath, new IntPtr(minimumSize), readOnly ? 1 : 0);
			return result.ToInt64();
		}

		/// <summary>
		/// Eject the current virtual SD card
		/// </summary>
		public void EjectSDCard()
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_ejectSDCard(_handle);
		}

		/// <summary>
		/// Set LVDSRX emulation state
		/// </summary>
		public void SetLvdsRXState(int state, int width, int height, double frequency)
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_setLvdsRXState(_handle, state, width, height, frequency);
		}

		/// <summary>
		/// Get write pointer to LVDSRX frame buffer
		/// </summary>
		public IntPtr GetLvdsRXWritePointer(long size)
		{
			ThrowIfDisposed();
			return BT8XXEMUNative.BT8XXEMU_lvdsRXWp(_handle, new IntPtr(size));
		}

		/// <summary>
		/// Commit a frame written to LVDSRX frame buffer
		/// </summary>
		public void AdvanceLvdsRXWritePointer()
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_advanceLvdsRXWp(_handle);
		}

		/// <summary>
		/// Stop the emulator
		/// </summary>
		public void Stop()
		{
			if (!_disposed && _handle != IntPtr.Zero)
			{
				BT8XXEMUNative.BT8XXEMU_stop(_handle);
			}
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(Emulator));
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (_handle != IntPtr.Zero)
				{
					BT8XXEMUNative.BT8XXEMU_destroy(_handle);
					_handle = IntPtr.Zero;
				}

				// Clear delegates
				_mcuSleepCallback = null;
				_graphicsCallback = null;
				_logCallback = null;
				_closeCallback = null;
				_mcuWakeCallback = null;

				_disposed = true;
			}
		}

		~Emulator()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}

	/// <summary>
	/// Parameters for creating an emulator instance
	/// </summary>
	public class EmulatorParameters
	{
		public BT8XXEMUNative.BT8XXEMU_EmulatorMode Mode { get; set; }

		public BT8XXEMUNative.BT8XXEMU_EmulatorFlags Flags { get; set; }
		public uint MousePressure { get; set; }
		public uint ExternalFrequency { get; set; }
		public uint ReduceGraphicsThreads { get; set; }
		public long RamGSizeBytes { get; set; }

		public Action<Emulator, int> MCUSleep { get; set; }
		public string RomFilePath { get; set; }
		public string OtpFilePath { get; set; }
		public string CoprocessorRomFilePath { get; set; }
		public Action<Emulator, GraphicsEventArgs> Graphics { get; set; }
		public EventHandler<LogEventArgs> Log { get; set; }
		public EventHandler Close { get; set; }
		public Flash Flash { get; set; }
		public string SDCardFilePath { get; set; }
		public Action<Emulator> MCUWake { get; set; }

		public EmulatorParameters()
		{
		}

		public static EmulatorParameters CreateDefault(BT8XXEMUNative.BT8XXEMU_EmulatorMode mode)
		{
			var nativeParams = new BT8XXEMUNative.BT8XXEMU_EmulatorParameters();
			BT8XXEMUNative.BT8XXEMU_defaults(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref nativeParams, mode);

			return new EmulatorParameters
			{
				Mode = mode,
				Flags = (BT8XXEMUNative.BT8XXEMU_EmulatorFlags)nativeParams.Flags,
				MousePressure = nativeParams.MousePressure,
				ExternalFrequency = nativeParams.ExternalFrequency,
				ReduceGraphicsThreads = nativeParams.ReduceGraphicsThreads,
				RamGSizeBytes = nativeParams.RamGSizeBytes.ToInt64(),
				RomFilePath = nativeParams.RomFilePath,
				OtpFilePath = nativeParams.OtpFilePath,
				CoprocessorRomFilePath = nativeParams.CoprocessorRomFilePath,
				SDCardFilePath = nativeParams.SDCardFilePath
			};
		}
	}

	/// <summary>
	/// Flash emulator wrapper
	/// </summary>
	public class Flash : IDisposable
	{
		private IntPtr _handle;
		private bool _disposed;
		private BT8XXEMUNative.BT8XXEMU_FlashParameters _parameters;
		private BT8XXEMUNative.FlashLogCallback _logCallback;
		private IntPtr _dataBuffer = IntPtr.Zero;

		public IntPtr Handle => _handle;

		/// <summary>
		/// Event raised when the flash logs a message
		/// </summary>
		public event EventHandler<LogEventArgs> Log;

		private Flash()
		{
		}

		/// <summary>
		/// Create a flash emulator with the specified parameters
		/// </summary>
		public static Flash Create(FlashParameters parameters)
		{
			if (parameters == null)
				throw new ArgumentNullException(nameof(parameters));

			var flash = new Flash();
			flash.Initialize(parameters);
			return flash;
		}

		/// <summary>
		/// Create a flash emulator with default parameters
		/// </summary>
		public static Flash CreateDefault()
		{
			return Create(FlashParameters.CreateDefault());
		}

		private void Initialize(FlashParameters parameters)
		{
			// Start with defaults
			_parameters = new BT8XXEMUNative.BT8XXEMU_FlashParameters();
			BT8XXEMUNative.BT8XXEMU_Flash_defaults(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref _parameters);

			// Apply user settings
			_parameters.DeviceType = parameters.DeviceType ?? _parameters.DeviceType;
			_parameters.SizeBytes = new IntPtr(parameters.SizeBytes);
			_parameters.DataFilePath = parameters.DataFilePath ?? string.Empty;
			_parameters.StatusFilePath = parameters.StatusFilePath ?? string.Empty;
			_parameters.Persistent = parameters.Persistent ? 1 : 0;
			_parameters.StdOut = parameters.StdOut ? 1 : 0;
			_parameters.UserContext = IntPtr.Zero;

			// Setup data buffer if provided
			if (parameters.Data != null && parameters.Data.Length > 0)
			{
				_dataBuffer = Marshal.AllocHGlobal(parameters.Data.Length);
				Marshal.Copy(parameters.Data, 0, _dataBuffer, parameters.Data.Length);
				_parameters.Data = _dataBuffer;
				_parameters.DataSizeBytes = new IntPtr(parameters.Data.Length);
			}

			// Setup log callback
			if (parameters.Log != null || Log != null)
			{
				_logCallback = (sender, context, type, message) =>
				{
					var messageStr = Marshal.PtrToStringAnsi(message);
					var args = new LogEventArgs(type, messageStr);
					parameters.Log?.Invoke(this, args);
					Log?.Invoke(this, args);
				};
				_parameters.Log = _logCallback;
			}

			_handle = BT8XXEMUNative.BT8XXEMU_Flash_create(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref _parameters);

			if (_handle == IntPtr.Zero)
				throw new InvalidOperationException("Failed to create flash emulator");
		}

		/// <summary>
		/// Transfer data using SPI or Quad SPI protocol
		/// </summary>
		public byte TransferSpi4(byte signal)
		{
			ThrowIfDisposed();
			return BT8XXEMUNative.BT8XXEMU_Flash_transferSpi4(_handle, signal);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(Flash));
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (_dataBuffer != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(_dataBuffer);
					_dataBuffer = IntPtr.Zero;
				}

				if (_handle != IntPtr.Zero)
				{
					BT8XXEMUNative.BT8XXEMU_Flash_destroy(_handle);
					_handle = IntPtr.Zero;
				}

				_logCallback = null;
				_disposed = true;
			}
		}

		~Flash()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}

	/// <summary>
	/// Parameters for creating a flash instance
	/// </summary>
	public class FlashParameters
	{
		public string DeviceType { get; set; }
		public long SizeBytes { get; set; }
		public string DataFilePath { get; set; }
		public string StatusFilePath { get; set; }
		public bool Persistent { get; set; }
		public bool StdOut { get; set; }
		public byte[] Data { get; set; }
		public EventHandler<LogEventArgs> Log { get; set; }

		public FlashParameters()
		{
		}

		public static FlashParameters CreateDefault()
		{
			var nativeParams = new BT8XXEMUNative.BT8XXEMU_FlashParameters();
			BT8XXEMUNative.BT8XXEMU_Flash_defaults(BT8XXEMUNative.BT8XXEMU_VERSION_API, ref nativeParams);
			return new FlashParameters
			{
				DeviceType = nativeParams.DeviceType,
				SizeBytes = nativeParams.SizeBytes.ToInt64(),
				DataFilePath = nativeParams.DataFilePath,
				StatusFilePath = nativeParams.StatusFilePath,
				Persistent = nativeParams.Persistent != 0,
				StdOut = nativeParams.StdOut != 0
			};
		}
	}

	/// <summary>
	/// High-resolution sleep/wake implementation
	/// </summary>
	public class SleepWake : IDisposable
	{
		private IntPtr _handle;
		private bool _disposed;

		private SleepWake(IntPtr handle)
		{
			_handle = handle;
		}

		/// <summary>
		/// Create a new SleepWake object
		/// </summary>
		public static SleepWake Create()
		{
			var handle = BT8XXEMUNative.BT8XXEMU_SleepWake_create();
			if (handle == IntPtr.Zero)
				throw new InvalidOperationException("Failed to create SleepWake object");

			return new SleepWake(handle);
		}

		/// <summary>
		/// Wake up from sleep (can be called from any thread)
		/// </summary>
		public void Wake()
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_SleepWake_wake(_handle);
		}

		/// <summary>
		/// Sleep for the specified number of milliseconds
		/// </summary>
		public void Sleep(int milliseconds)
		{
			ThrowIfDisposed();
			BT8XXEMUNative.BT8XXEMU_SleepWake_sleep(_handle, milliseconds);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(SleepWake));
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (_handle != IntPtr.Zero)
				{
					BT8XXEMUNative.BT8XXEMU_SleepWake_destroy(_handle);
					_handle = IntPtr.Zero;
				}
				_disposed = true;
			}
		}

		~SleepWake()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}

	#region Event Args

	/// <summary>
	/// Event arguments for log events
	/// </summary>
	public class LogEventArgs : EventArgs
	{
		public BT8XXEMUNative.BT8XXEMU_LogType Type { get; }
		public string Message { get; }

		public LogEventArgs(BT8XXEMUNative.BT8XXEMU_LogType type, string message)
		{
			Type = type;
			Message = message;
		}
	}

	/// <summary>
	/// Event arguments for graphics output events
	/// </summary>
	public class GraphicsEventArgs : EventArgs
	{
		public bool Output { get; }
		public IntPtr Buffer { get; }
		public uint Width { get; }
		public uint Height { get; }
		public BT8XXEMUNative.BT8XXEMU_FrameFlags Flags { get; }
		public bool ContinueRunning { get; set; } = true;

		public GraphicsEventArgs(bool output, IntPtr buffer, uint width, uint height, BT8XXEMUNative.BT8XXEMU_FrameFlags flags)
		{
			Output = output;
			Buffer = buffer;
			Width = width;
			Height = height;
			Flags = flags;
		}

		/// <summary>
		/// Copy the buffer data to a managed byte array (ARGB8888 format)
		/// </summary>
		public byte[] CopyBuffer()
		{
			if (Buffer == IntPtr.Zero || Width == 0 || Height == 0)
				return null;

			int size = (int)(Width * Height * 4); // 4 bytes per pixel (ARGB8888)
			byte[] data = new byte[size];
			Marshal.Copy(Buffer, data, 0, size);
			return data;
		}

		/// <summary>
		/// Check if a specific flag is set
		/// </summary>
		public bool HasFlag(BT8XXEMUNative.BT8XXEMU_FrameFlags flag)
		{
			return (Flags & flag) == flag;
		}
	}

	#endregion
}