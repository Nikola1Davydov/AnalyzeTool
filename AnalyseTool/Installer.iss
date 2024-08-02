; Script generated by the Inno Setup Script Wizard.
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

[Setup]
; Имя вашего инсталлятора
AppName=AnalyseTool Plugin for Revit
AppVersion=1.0
; Папка установки. Используйте {autopf} для автоматического выбора Program Files
DefaultDirName={autopf}\AnalyseTool Plugin for Revit
; Папка в меню "Пуск"
DefaultGroupName=AnalyseTool Plugin for Revit
; Описание вашего приложения
AppPublisher=Nikola1 Davydov
AppPublisherURL=https://github.com/Nikola1Davydov/AnalyzeTool
AppSupportURL=https://github.com/Nikola1Davydov/AnalyzeTool
AppUpdatesURL=https://github.com/Nikola1Davydov/AnalyzeTool
; Тип установки
Compression=lzma
SolidCompression=yes
OutputDir=.
OutputBaseFilename=AnalyseToolPluginSetup
LicenseFile=license.txt

[Languages]
; Указание языка установки
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Определение задач для установки в разные версии Revit
Name: "install2023"; Description: "Install for Revit 2023"; GroupDescription: "Revit versions:"; Flags: unchecked
Name: "install2024"; Description: "Install for Revit 2024"; GroupDescription: "Revit versions:"; Flags: unchecked
Name: "install2025"; Description: "Install for Revit 2025"; GroupDescription: "Revit versions:"; Flags: unchecked

[Files]
; Копирование файлов плагина в соответствующие папки Addins для выбранных версий Revit
Source: "D:\12_Code\C# Lernen\RevitPlugins\AnalyseTool\bin\Release R23\publish\Revit 2023 Release R23 addin\*"; DestDir: "{code:GetAddinsDir|2023}"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: install2023
Source: "D:\12_Code\C# Lernen\RevitPlugins\AnalyseTool\bin\Release R24\publish\Revit 2024 Release R24 addin\*"; DestDir: "{code:GetAddinsDir|2024}"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: install2024
Source: "D:\12_Code\C# Lernen\RevitPlugins\AnalyseTool\bin\Release R25\publish\Revit 2025 Release R25 addin\*"; DestDir: "{code:GetAddinsDir|2025}"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: install2025

[UninstallDelete]
Type: filesandordirs; Name: "{code:GetAddinsDir|2023}\*"; Tasks: install2023
Type: filesandordirs; Name: "{code:GetAddinsDir|2024}\*"; Tasks: install2024
Type: filesandordirs; Name: "{code:GetAddinsDir|2025}\*"; Tasks: install2025

[Code]
function GetAddinsDir(Param: string): string;
begin
  if Param = '2023' then
    Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2023')
  else if Param = '2024' then
    Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2024')
  else if Param = '2025' then
    Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2025');
end;
