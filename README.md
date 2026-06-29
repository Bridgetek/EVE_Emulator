# EVE Emulator Release Package

This repository provides the binary release and public header files for the
BT8XX/EVE emulator, together with example projects that show how to launch and
drive the emulator from end-user applications.

The emulator itself is distributed here as prebuilt binaries. Application code
can link against the provided import library/header from C or C++, or call the
DLL from C# through the included P/Invoke wrapper examples.

## Repository Layout

```text
bin/
  bt8xxemu.dll      Runtime DLL for the emulator
  mx25lemu.dll      Runtime dependency for flash emulation
  zlib.dll          Runtime compression dependency

include/
  bt8xxemu.h        Public emulator API header

lib/
  bt8xxemu.lib      Windows import library for C/C++ projects

examples/
  CSharp-BT81X/     C# WinForms example for BT81X devices
  CSharp-BT82X/     C# WinForms example for BT82X devices
```

## Using the Emulator in a C or C++ Project

1. Add `include/` to your compiler include directories.
2. Add `lib/` to your linker library directories.
3. Link your application with `bt8xxemu.lib`.
4. Deploy the runtime DLLs from `bin/` next to your executable, or make sure
   they are available on the process `PATH`:
   - `bt8xxemu.dll`
   - `mx25lemu.dll`
   - `zlib.dll`

Include the public API with:

```cpp
#include "bt8xxemu.h"
```

The current public API version exposed by the header is `BT8XXEMU_VERSION_API`
`16`.

## Running the C# Examples

The C# examples are Visual Studio/MSBuild projects targeting .NET Framework
3.5. Each project includes:

- `BT8XXEMUNative.cs`: raw P/Invoke declarations for `bt8xxemu.dll`
- `BT8XXEMU.cs`: managed helper/wrapper code
- `MainWindow.cs`: sample WinForms UI and emulator usage

To run an example:

1. Open one of the solution files in Visual Studio:
   - `examples/CSharp-BT81X/BT8XXEMUCSharpDemo.sln`
   - `examples/CSharp-BT82X/BT8XXEMUCSharpDemo.sln`
2. Build the solution.
3. Run the generated `BT8XXEMUCSharpDemo.exe`.

The projects contain post-build steps that copy the required runtime DLLs from
the repository `bin/` directory into the example output directory. The BT81X
example also copies `flash-817-default.bin` for its flash-emulation setup.

## Redistributing an Application

When distributing an application that uses the emulator, include the executable
or library you built along with the runtime files from `bin/` that it depends
on. For the included examples, all three DLLs in `bin/` are expected to be
present next to the executable.

## Notes

- This repository is intended for emulator release artifacts and integration
  examples, not for building the emulator library itself from source.
- Keep the DLLs, import library, and public headers from the same release
  together to avoid API or ABI mismatches.
