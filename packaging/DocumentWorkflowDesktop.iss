#ifndef AppVersion
  #error AppVersion must be supplied by Build-Release.ps1
#endif
#ifndef PublishRoot
  #error PublishRoot must be supplied by Build-Release.ps1
#endif
#ifndef OutputRoot
  #error OutputRoot must be supplied by Build-Release.ps1
#endif

[Setup]
AppId={{BA711D53-316D-48C9-85A1-E950322F8E92}
AppName=Document Workflow Desktop
AppVersion={#AppVersion}
AppPublisher=Document Workflow Desktop contributors
AppPublisherURL=https://github.com/dstojanoviccc/document-workflow-desktop
DefaultDirName={localappdata}\Programs\DocumentWorkflowDesktop
DefaultGroupName=Document Workflow Desktop
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
OutputDir={#OutputRoot}
OutputBaseFilename=DocumentWorkflowDesktop-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DocumentWorkflow.App.exe
VersionInfoVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Document Workflow Desktop"; Filename: "{app}\DocumentWorkflow.App.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\DocumentWorkflow.App.exe"; Description: "Launch Document Workflow Desktop"; Flags: nowait postinstall skipifsilent

; No user data is installed here and no UninstallDelete entry targets it.
; Stable AppId supports upgrades. Uninstall removes only tracked application files.
