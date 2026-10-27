@echo off
cd /d "C:\Program Files (x86)\NoteBook FanControl"
echo === READ ===
ec-probe.exe read 0x10
ec-probe.exe read 0x13
ec-probe.exe read 0x15
ec-probe.exe read 0x21
ec-probe.exe read 0x22
ec-probe.exe read 0x37
ec-probe.exe read 0x3A
echo === WRITE ===
ec-probe.exe write 0x22 12
ec-probe.exe write 0x21 48
ec-probe.exe write 0x37 100
ec-probe.exe write 0x3A 100
timeout /t 1 >nul
echo === AFTER ===
ec-probe.exe read 0x22
ec-probe.exe read 0x21
ec-probe.exe read 0x37
ec-probe.exe read 0x3A
ec-probe.exe read 0x13
ec-probe.exe read 0x15
echo === RESET ===
ec-probe.exe write 0x22 4
ec-probe.exe write 0x21 16
ec-probe.exe write 0x37 0
ec-probe.exe write 0x3A 0
pause
