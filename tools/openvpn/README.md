# OpenVPN Bundled Executable

This directory should contain the OpenVPN executable and its dependencies for bundling with the application.

## Required Files

Download OpenVPN Community Edition from: https://openvpn.net/community-downloads/

Extract and place the following files in this directory:

### Core Executable
- `openvpn.exe` - Main OpenVPN executable

### Required Dependencies (typically from OpenVPN installation)
- `libeay32.dll` - OpenSSL crypto library
- `ssleay32.dll` - OpenSSL SSL library
- `libcrypto-1_1.dll` - OpenSSL crypto (newer versions)
- `libssl-1_1.dll` - OpenSSL SSL (newer versions)
- `msvcr120.dll` - Microsoft Visual C++ Runtime (if needed)

### TAP Driver (if needed)
The TAP driver installation may still be required on target systems. Consider including:
- TAP driver installer or instructions for users

## Build Integration

The application will automatically detect and use the bundled OpenVPN executable in this location with highest priority over system installations.

## Notes

- Ensure you comply with OpenVPN licensing requirements when redistributing
- Test thoroughly on target systems without OpenVPN pre-installed
- Consider including version information for troubleshooting