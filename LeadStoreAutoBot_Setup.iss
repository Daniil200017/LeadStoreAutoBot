; Inno Setup-скрипт для C# / WPF / .NET 9 версии бота.
; Собирает single-file self-contained exe (~150 МБ внутри установщика).

#define AppName "LeadStore Auto Bot"
#define AppVersion "2.0"
#define AppExeName "LeadStoreAutoBot.exe"

[Setup]
AppId={{C8D9E0F1-A2B3-4567-CDEF-012345678901}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LeadStore
DefaultDirName={autopf}\LeadStoreAutoBot
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=LeadStoreAutoBot_Setup
SetupIconFile=..\leadstore_bot.ico
WizardImageFile=..\installer_banner.png
WizardSmallImageFile=..\installer_small.png
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
ShowLanguageDialog=no
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"

[Files]
; Пакуем всё содержимое publish-папки рекурсивно
; (LeadStoreAutoBot.exe + Resources/Sounds/*.wav + selenium-manager/*)
Source: "LeadStoreAutoBot\bin\Release\net9.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Иконка
Source: "..\leadstore_bot.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\leadstore_bot.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\leadstore_bot.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
