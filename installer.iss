#define AppName      "TARA-Flasher"
#define AppVersion   "1.0"
#define AppPublisher "TARA"
#define AppExe       "TARA-Flasher.exe"
#define SrcDir       "publish_out"

[Setup]
AppId={{E4A9C1B2-7F3D-4E8A-BC12-6D5F2A930E47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installer_out
OutputBaseFilename=TARA-Flasher-Setup
SetupIconFile=publish_out\logo.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

[Code]
function IsDotNet48Installed(): Boolean;
var
  releaseKey: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', releaseKey) and (releaseKey >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  if not IsDotNet48Installed() then
  begin
    MsgBox('.NET Framework 4.8 is required.' + #13#10 +
           'Please install it from Microsoft first.',
           mbCriticalError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#SrcDir}\{#AppExe}";                                  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\TARA-Flasher.exe.config";                    DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\logo.ico";                                   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\logo.png";                                   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\Microsoft.Bcl.AsyncInterfaces.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Buffers.dll";                         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.IO.Pipelines.dll";                    DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Memory.dll";                          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Numerics.Vectors.dll";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Text.Encodings.Web.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Text.Json.dll";                       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.Threading.Tasks.Extensions.dll";      DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\System.ValueTuple.dll";                      DestDir: "{app}"; Flags: ignoreversion
; STM32CubeProgrammer installer — included automatically if placed next to installer.iss
; Supports common naming conventions: SetupSTM32CubeProgrammer_vX.X.X.exe / STM32CubeProgrammer_vX.X.exe
Source: "SetupSTM32CubeProgrammer*.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "STM32CubeProgrammer_*.exe";     DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "en.STM32CubeProgrammer*.exe";   DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"; IconFilename: "{app}\logo.ico"
Name: "{group}\Uninstall {#AppName}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; IconFilename: "{app}\logo.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
