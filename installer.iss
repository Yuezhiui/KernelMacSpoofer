#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef SourceDir
  #define SourceDir "MacSpoof_Fixed"
#endif

[Setup]
; Keep the legacy AppId stable so existing installations upgrade cleanly.
AppId=KernelMacSpoofer
AppName=MacSpoof
AppVersion={#AppVersion}
AppPublisher=Zhi
AppPublisherURL=https://github.com/Yuezhiui/KernelMacSpoofer
AppSupportURL=https://github.com/Yuezhiui/KernelMacSpoofer/issues
AppUpdatesURL=https://github.com/Yuezhiui/KernelMacSpoofer/releases
DefaultDirName={autopf}\MacSpoof
DefaultGroupName=MacSpoof
UninstallDisplayName=MacSpoof
UninstallDisplayIcon={app}\MacSpoof.exe
LicenseFile=LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=artifacts
OutputBaseFilename=MacSpoof-Setup-v{#AppVersion}-x64
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoCompany=Zhi
VersionInfoDescription=MacSpoof Windows installer
VersionInfoProductName=MacSpoof
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MacSpoof"; Filename: "{app}\MacSpoof.exe"
Name: "{autodesktop}\MacSpoof"; Filename: "{app}\MacSpoof.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
