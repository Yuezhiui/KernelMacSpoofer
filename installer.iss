#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "MacSpoof_Fixed"
#endif

[Setup]
AppId=KernelMacSpoofer
AppName=KernelMacSpoofer
AppVersion={#AppVersion}
AppPublisher=Zhi
AppPublisherURL=https://github.com/Yuezhiui/KernelMacSpoofer
AppSupportURL=https://github.com/Yuezhiui/KernelMacSpoofer/issues
AppUpdatesURL=https://github.com/Yuezhiui/KernelMacSpoofer/releases
DefaultDirName={autopf}\KernelMacSpoofer
DefaultGroupName=KernelMacSpoofer
UninstallDisplayIcon={app}\MacSpoof.exe
LicenseFile=LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=artifacts
OutputBaseFilename=KernelMacSpoofer-Setup-v{#AppVersion}-x64
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MacSpoof"; Filename: "{app}\MacSpoof.exe"
Name: "{autodesktop}\MacSpoof"; Filename: "{app}\MacSpoof.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
