for /f %%f IN (file.lst) DO copy "E:\GitHub\ShipyardMod\ShipyardMod\%%f" "C:\Users\Brant\AppData\Roaming\SpaceEngineers\Mods\ShipyardMod\Data\Scripts\ShipyardMod"
"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64\SEWorkshopTool.exe" --upload --compile --mods ShipyardMod
@echo off
pause