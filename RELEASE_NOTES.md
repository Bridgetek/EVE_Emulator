# EVE Emulator Release Notes

## Version 5.1.26 - September 25, 2026

### Fixes

- Fixed bitmap and ASTC RAM address masking so that addresses correctly match
  the configured RAM size.

### Performance

- Improved the performance of hardware-matching line, rectangle, and point
  rendering.
- Added the `BT8XXEMU_MATCH_OPTIMIZED` option. This optimization is enabled by
  default.

### Dependencies

- Removed the dependency on the third-party ASTC library. Rendering already
  uses the faster hardware-matching ASTC decoder.
