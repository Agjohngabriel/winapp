@echo OFF
rem Automatically set PATH to openssl.exe
FOR /F "tokens=2*" %%a IN ('REG QUERY "HKEY_LOCAL_MACHINE\SOFTWARE\OpenVPN" /v bin_dir') DO set "PATH=%PATH%;%%b"
bin\sh.exe bin\easyrsa-shell-init.sh
