<#
.SYNOPSIS
    Drives the Code Janitor VS commands in the Visual Studio Experimental instance, unattended, and verifies the results.
.DESCRIPTION
    Covers what the unit tests cannot reach: the commands as Visual Studio runs them (DTE.ExecuteCommand), editor buffers,
    Solution Explorer selection, modal dialogs and the files left on disk.

    Steps:
      1. Installs the VSIX into the Experimental hive with scripts/deploy-exp.ps1 (skip with -SkipDeploy).
      2. Creates a temporary solution (net8.0 project, dirty C# fixtures, .editorconfig, .codejanitor, a solution-specific
         CodeJanitor.config so no setting is ever written to the user's CodeJanitor.config, and a git repository).
      3. Starts `devenv.exe /rootsuffix Exp <solution>` and attaches to it through the COM running object table
         ("!VisualStudio.DTE.<version>:<pid>").
      4. Runs the scenarios below, printing PASS/FAIL per check, and exits 0 only when every check passed.
      5. Kills ONLY the Experimental devenv.exe it started and deletes the temporary directory.

    Scenarios (use -Scenario to run a subset): Startup, ActiveDocument, LeadingBlankLine, OpenCode, SelectedScope,
    CleanupAllCode, ChangedFiles, AutoCleanupOnSave, FixNamespace, RemoveAllRegions, Reorganize, SortAndJoinLines, Options,
    ExternalFile, ReadOnly, Undo, Build, OutputPane, UserSettings.

    Modal dialogs are found by enumerating the top-level windows of the Experimental devenv process (EnumWindows /
    GetWindowThreadProcessId). Native message boxes are answered by posting BM_CLICK to their button; WPF dialogs
    (Cleanup Options, Cleanup External File, Tools > Options) expose no Win32 buttons and are answered through UI
    Automation InvokePattern. No mouse or keyboard input is simulated. A dialog nobody expects is closed and fails the
    scenario it appeared in.

    Runs under Windows PowerShell 5.1 in an STA thread (it relaunches itself that way when started from pwsh or an MTA
    host). Deploy and build steps hold the global mutex 'Global\cj-build-test' shared with the unit test runner.
.PARAMETER Configuration
    VSIX build configuration passed to deploy-exp.ps1 (Debug or Release). Defaults to Release.
.PARAMETER SkipDeploy
    Do not build and install the VSIX; test whatever is currently installed in the Experimental hive.
.PARAMETER Scenario
    Scenario names or wildcards to run. Defaults to all.
.PARAMETER StartupTimeoutSeconds
    How long to wait for the Experimental instance to load the solution. Defaults to 300.
.PARAMETER CommandTimeoutSeconds
    How long a single command (including its dialogs) may take. Defaults to 240.
.PARAMETER TotalTimeoutMinutes
    Kills the Experimental instance when the whole run exceeds this. Defaults to 60.
.PARAMETER KeepWorkspace
    Keeps the temporary solution directory and prints its path.
.EXAMPLE
    powershell -STA -File scripts\test-exp-integration.ps1
.EXAMPLE
    powershell -STA -File scripts\test-exp-integration.ps1 -SkipDeploy -Scenario ActiveDocument,Undo
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipDeploy,
    [string[]]$Scenario = @('*'),
    [int]$StartupTimeoutSeconds = 300,
    [int]$CommandTimeoutSeconds = 240,
    [int]$TotalTimeoutMinutes = 60,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $forward = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($key in $PSBoundParameters.Keys) {
        $value = $PSBoundParameters[$key]
        if ($value -is [switch]) {
            if ($value.IsPresent) { $forward += "-$key" }
        }
        elseif ($value -is [array]) {
            $forward += "-$key"
            $forward += ($value -join ',')
        }
        else {
            $forward += "-$key"
            $forward += [string]$value
        }
    }

    & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') @forward
    exit $LASTEXITCODE
}

$Scenario = @($Scenario | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

$repoRoot = Split-Path $PSScriptRoot -Parent
$deployScript = Join-Path $PSScriptRoot 'deploy-exp.ps1'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

public class CjWindow
{
    public IntPtr Handle;
    public IntPtr Owner;
    public string Title;
    public string ClassName;
    public bool Visible;
    public int Id;
}

public static class CjNative
{
    private delegate bool EnumProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    private static CjWindow Describe(IntPtr handle)
    {
        var title = new StringBuilder(512);
        var className = new StringBuilder(256);
        GetWindowText(handle, title, title.Capacity);
        GetClassName(handle, className, className.Capacity);
        return new CjWindow
        {
            Handle = handle,
            Owner = GetWindow(handle, 4),
            Title = title.ToString(),
            ClassName = className.ToString(),
            Visible = IsWindowVisible(handle),
            Id = GetDlgCtrlID(handle)
        };
    }

    public static CjWindow[] GetWindows(int processId)
    {
        var found = new List<CjWindow>();
        EnumWindows(delegate(IntPtr handle, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(handle, out pid);
            if (pid == (uint)processId) { found.Add(Describe(handle)); }
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public static CjWindow[] GetChildren(IntPtr parent)
    {
        var found = new List<CjWindow>();
        EnumChildWindows(parent, delegate(IntPtr handle, IntPtr lParam)
        {
            found.Add(Describe(handle));
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public static void Click(IntPtr button) { PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); }

    public static void Close(IntPtr window) { PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}

public static class CjWatchdog
{
    private static System.Threading.Timer timer;

    public static void Start(int processId, int milliseconds)
    {
        timer = new System.Threading.Timer(delegate(object state)
        {
            try { System.Diagnostics.Process.GetProcessById(processId).Kill(); } catch (Exception) { }
        }, null, milliseconds, System.Threading.Timeout.Infinite);
    }

    public static void Stop()
    {
        if (timer != null) { timer.Dispose(); timer = null; }
    }
}

public static class Rot
{
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

    public static object Find(string name)
    {
        IRunningObjectTable rot;
        GetRunningObjectTable(0, out rot);
        IEnumMoniker monikers;
        rot.EnumRunning(out monikers);
        var moniker = new IMoniker[1];
        while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
        {
            IBindCtx ctx;
            CreateBindCtx(0, out ctx);
            string displayName;
            moniker[0].GetDisplayName(ctx, null, out displayName);
            if (displayName == name)
            {
                object found;
                rot.GetObject(moniker[0], out found);
                return found;
            }
        }
        return null;
    }
}

[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int callType, IntPtr caller, int tickCount, IntPtr interfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType);
    [PreserveSig] int MessagePending(IntPtr callee, int tickCount, int pendingType);
}

public class RetryFilter : IOleMessageFilter
{
    [DllImport("ole32.dll")] private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);

    public static void Register()
    {
        IOleMessageFilter old;
        CoRegisterMessageFilter(new RetryFilter(), out old);
    }

    int IOleMessageFilter.HandleInComingCall(int callType, IntPtr caller, int tickCount, IntPtr interfaceInfo) { return 0; }
    int IOleMessageFilter.RetryRejectedCall(IntPtr callee, int tickCount, int rejectType) { return rejectType == 2 ? 200 : -1; }
    int IOleMessageFilter.MessagePending(IntPtr callee, int tickCount, int pendingType) { return 2; }
}
'@
[RetryFilter]::Register()

$script:Results = New-Object System.Collections.Generic.List[object]
$script:CurrentScenario = ''
$script:Aborted = $false
$script:VsPid = 0
$script:Dte = $null
$script:Commands = @{}
$script:Workspace = $null
$script:SolutionPath = $null
$script:UserConfigPath = Join-Path $env:LOCALAPPDATA 'CodeJanitor\CodeJanitor.config'
$script:UserAutoSaveAtStart = $null
$script:DialogRules = @()
$script:DialogLog = New-Object System.Collections.Generic.List[string]
$script:Unexpected = New-Object System.Collections.Generic.List[string]
$script:Answered = @{}
$script:MainWindow = [IntPtr]::Zero
$script:HasGit = $false
$script:DismissUnknown = $false
$script:FirstSeen = @{}
$script:Warned = @{}

$ProjectGuid = '{9A19103F-16F7-4668-BE54-9A1E7A4F22E0}'
$CodeViewKind = '{7651A701-06E5-11D1-8EBD-00A0C90F26EA}'
$SolutionExplorerKind = '{3AE79031-E1BC-11D0-8F78-00A0C9110057}'

# ---------------------------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------------------------

function Show-Text {
    param([string]$Text, [int]$Max = 400)
    if ($null -eq $Text) { return '<null>' }
    $shown = $Text.Replace("`r", '\r').Replace("`n", '\n')
    if ($shown.Length -gt $Max) { $shown = $shown.Substring(0, $Max) + '...' }
    return $shown
}

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $script:Results.Add([pscustomobject]@{ Scenario = $script:CurrentScenario; Check = $Name; Ok = $Ok; Detail = $Detail })
    if ($Ok) {
        Write-Host ("PASS  {0} :: {1}" -f $script:CurrentScenario, $Name) -ForegroundColor Green
    }
    else {
        Write-Host ("FAIL  {0} :: {1}{2}" -f $script:CurrentScenario, $Name, $(if ($Detail) { " -- $Detail" } else { '' })) -ForegroundColor Red
    }
}

function Write-Info {
    param([string]$Message)
    Write-Host "      $Message" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------------------------
# Build mutex shared with the unit test runner
# ---------------------------------------------------------------------------------------------

function Use-BuildMutex {
    param([scriptblock]$Body)
    $mutex = New-Object System.Threading.Mutex($false, 'Global\cj-build-test')
    try {
        try { [void]$mutex.WaitOne() } catch [System.Threading.AbandonedMutexException] { }
        & $Body
    }
    finally {
        try { $mutex.ReleaseMutex() } catch { }
        $mutex.Dispose()
    }
}

# ---------------------------------------------------------------------------------------------
# COM helpers
# ---------------------------------------------------------------------------------------------

function Com {
    param([scriptblock]$Body, [int]$Attempts = 20)
    $busy = @(-2147418111, -2147417846)
    for ($i = 1; ; $i++) {
        try {
            return (& $Body)
        }
        catch {
            $com = $_.Exception
            while ($com -and -not ($com -is [System.Runtime.InteropServices.COMException])) { $com = $com.InnerException }
            if (-not $com -or ($busy -notcontains $com.HResult) -or $i -ge $Attempts) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Get-VsDocument {
    param([string]$Path)
    return Com {
        foreach ($document in $script:Dte.Documents) {
            if ($document.FullName -ieq $Path) { return $document }
        }
        return $null
    }
}

function Open-ProjectFile {
    param([string]$Path)
    $item = $null
    for ($i = 0; $i -lt 30 -and -not $item; $i++) {
        $item = Com { $script:Dte.Solution.FindProjectItem($Path) }
        if (-not $item) { Start-Sleep -Seconds 1 }
    }
    if (-not $item) { throw "Not a project item of the open solution: $Path" }
    $window = Com { $item.Open($CodeViewKind) }
    Com { $window.Activate() }
    return Get-VsDocument $Path
}

function Open-ExternalFile {
    param([string]$Path)
    $null = Com { $script:Dte.ItemOperations.OpenFile($Path) }
    $document = $null
    for ($i = 0; $i -lt 20 -and -not $document; $i++) {
        $document = Get-VsDocument $Path
        if (-not $document) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $document) { throw "Could not open external file: $Path" }
    Com { $document.Activate() }
    return $document
}

function Get-BufferText {
    param([string]$Path)
    $document = Get-VsDocument $Path
    if (-not $document) { throw "Document is not open: $Path" }
    return Com {
        $text = $document.Object('TextDocument')
        $text.StartPoint.CreateEditPoint().GetText($text.EndPoint)
    }
}

function Set-BufferText {
    param([string]$Path, [string]$Text)
    $document = Get-VsDocument $Path
    if (-not $document) { throw "Document is not open: $Path" }
    Com {
        $buffer = $document.Object('TextDocument')
        $buffer.StartPoint.CreateEditPoint().ReplaceText($buffer.EndPoint, $Text, 0)
    }
}

function Set-Selection {
    param([string]$Path, [int]$StartLine, [int]$EndLine = 0, [switch]$ToEndOfLine)
    $document = Get-VsDocument $Path
    Com {
        $selection = $document.Object('TextDocument').Selection
        $selection.MoveToLineAndOffset($StartLine, 1, $false)
        if ($EndLine -gt 0) {
            $selection.MoveToLineAndOffset($EndLine, 1, $true)
            if ($ToEndOfLine) { $selection.EndOfLine($true) }
        }
    }
}

function Close-AllDocuments {
    Com { $script:Dte.Documents.CloseAll(2) }
}

function Select-SolutionItem {
    param([string[]]$Segments)
    $window = Com { $script:Dte.Windows.Item($SolutionExplorerKind) }
    if (-not $window) { throw 'The Solution Explorer window was not found.' }
    Com { $window.Activate() }
    $explorer = Com { $window.Object }
    if (-not $explorer) { throw 'The Solution Explorer window exposes no UIHierarchy.' }
    $root = Com { $explorer.UIHierarchyItems }
    $item = Com { $root.Item(1) }
    if (-not $item) { throw 'Solution Explorer has no root item.' }
    foreach ($segment in $Segments) {
        Com { $item.UIHierarchyItems.Expanded = $true }
        $item = Com { $item.UIHierarchyItems.Item($segment) }
    }
    Com { $item.Select(1) }
}

function Wait-VsIdle {
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        $state = Com { $script:Dte.Solution.SolutionBuild.BuildState }
        if ($state -ne 2) { return }
        Invoke-DialogPass
        Start-Sleep -Milliseconds 500
    }
}

function Get-StatusBarText {
    return Com { $script:Dte.StatusBar.Text }
}

# ---------------------------------------------------------------------------------------------
# Modal dialog handling (Win32 enumeration, BM_CLICK for native dialogs, UI Automation for WPF dialogs)
# ---------------------------------------------------------------------------------------------

function New-DialogRule {
    param([string]$Title, $Button)
    return @{ Title = $Title; Button = $Button }
}

function Set-DialogRules {
    param([object[]]$Rules = @())
    $script:DialogRules = @($Rules) + @(
        (New-DialogRule '^CodeJanitor: Cleanup Progress' $null),
        (New-DialogRule '^CodeJanitor Add XMLDoc' $null)
    )
    $script:DialogLog.Clear()
}

function Test-NativeButton {
    param($Button, [string]$Pattern)
    $ids = @{ Yes = 6; No = 7; OK = 1; Cancel = 2; Close = 2 }
    foreach ($name in $ids.Keys) {
        if ($name -match $Pattern -and $Button.Id -eq $ids[$name]) { return $true }
    }
    return $false
}

function Invoke-DialogButton {
    param($Window, [string]$ButtonPattern)
    if ($Window.ClassName -eq '#32770') {
        foreach ($child in [CjNative]::GetChildren($Window.Handle)) {
            if ($child.ClassName -eq 'Button' -and (Test-NativeButton $child $ButtonPattern)) {
                [CjNative]::Click($child.Handle)
                return $true
            }
        }
        return $false
    }

    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Window.Handle)
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        foreach ($button in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            $name = ($button.Current.Name -replace '[_&]', '')
            if ($name -match $ButtonPattern) {
                $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $pattern.Invoke()
                return $true
            }
        }
    }
    catch { }
    return $false
}

function Invoke-DialogPass {
    if ($script:VsPid -le 0) { return }
    $now = Get-Date
    foreach ($window in [CjNative]::GetWindows($script:VsPid)) {
        if (-not $window.Visible -or [string]::IsNullOrEmpty($window.Title) -or $window.Handle -eq $script:MainWindow) { continue }
        $key = [string]$window.Handle
        if (-not $script:FirstSeen.ContainsKey($key)) { $script:FirstSeen[$key] = $now }
        if ($script:Answered.ContainsKey($key) -and ($now - $script:Answered[$key]).TotalSeconds -lt 3) { continue }

        $rule = $null
        foreach ($candidate in $script:DialogRules) {
            if ($window.Title -match $candidate.Title) { $rule = $candidate; break }
        }

        if ($rule) {
            if ($null -eq $rule.Button) { continue }
            $script:Answered[$key] = $now
            if (Invoke-DialogButton $window $rule.Button) {
                $script:DialogLog.Add($window.Title)
                Write-Info "dialog '$($window.Title)' answered '$($rule.Button)'"
            }
            elseif (($now - $script:FirstSeen[$key]).TotalSeconds -gt 8) {
                $script:DialogLog.Add($window.Title)
                if (-not $script:Warned.ContainsKey($key)) {
                    $script:Warned[$key] = $true
                    Write-Info "dialog '$($window.Title)' has no clickable '$($rule.Button)' button - closing it"
                }
                [CjNative]::Close($window.Handle)
            }
            continue
        }

        if ($window.ClassName -eq '#32770' -or $window.Owner -ne [IntPtr]::Zero) {
            $script:Answered[$key] = $now
            if (-not $script:DismissUnknown) { continue }
            $script:Unexpected.Add("$($window.Title) [$($window.ClassName)]")
            Write-Info "unexpected dialog '$($window.Title)' [$($window.ClassName)] - dismissing"
            if (-not (Invoke-DialogButton $window '^(OK|Cancel|No|Close)$')) { [CjNative]::Close($window.Handle) }
        }
    }
}

function Get-StrayDialogs {
    @([CjNative]::GetWindows($script:VsPid) | Where-Object {
            $_.Visible -and -not [string]::IsNullOrEmpty($_.Title) -and $_.Handle -ne $script:MainWindow -and
            ($_.ClassName -eq '#32770' -or $_.Owner -ne [IntPtr]::Zero) -and $_.Title -notmatch 'Microsoft Visual Studio'
        })
}

function Close-StrayWindows {
    if ($script:VsPid -le 0) { return }
    for ($round = 0; $round -lt 5; $round++) {
        $strays = Get-StrayDialogs
        if ($strays.Count -eq 0) { return }
        foreach ($window in $strays) {
            Write-Info "closing dialog '$($window.Title)' left open by scenario '$script:CurrentScenario'"
            if (-not (Invoke-DialogButton $window '^(Cancel|Anuluj|Close|Zamknij|No|Nie|OK)$')) { [CjNative]::Close($window.Handle) }
        }
        Start-Sleep -Milliseconds 800
    }
    Check 'no dialog is left open after the scenario' $false ((Get-StrayDialogs | ForEach-Object { $_.Title }) -join '; ')
}

function Test-DialogSeen {
    param([string]$TitlePattern)
    foreach ($title in $script:DialogLog) {
        if ($title -match $TitlePattern) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------------------------
# Running VS commands. ExecuteCommand blocks while the command shows a modal dialog, so it runs on its own
# STA runspace while this thread answers dialogs.
# ---------------------------------------------------------------------------------------------

$script:CommandBody = {
    param($ProcessId, $CommandName, $CommandArguments)
    try {
        [RetryFilter]::Register()
        $dte = $null
        foreach ($version in '18.0', '17.0') {
            $dte = [Rot]::Find("!VisualStudio.DTE.${version}:$ProcessId")
            if ($dte) { break }
        }
        if (-not $dte) { throw "DTE for process $ProcessId was not found in the running object table." }
        $dte.ExecuteCommand($CommandName, $CommandArguments)
        return @{ Ok = $true; Error = '' }
    }
    catch {
        return @{ Ok = $false; Error = $_.Exception.Message }
    }
}

function Invoke-DteCommand {
    param([string]$Name, [string]$Arguments = '', [int]$TimeoutSeconds = $CommandTimeoutSeconds)
    for ($attempt = 1; ; $attempt++) {
        $runspace = [runspacefactory]::CreateRunspace()
        $runspace.ApartmentState = 'STA'
        $runspace.Open()
        $shell = [powershell]::Create()
        $shell.Runspace = $runspace
        [void]$shell.AddScript($script:CommandBody).AddArgument($script:VsPid).AddArgument($Name).AddArgument($Arguments)
        $async = $shell.BeginInvoke()
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $timedOut = $false
        while (-not $async.IsCompleted) {
            Invoke-DialogPass
            if ((Get-Date) -gt $deadline) { $timedOut = $true; break }
            Start-Sleep -Milliseconds 250
        }

        if ($timedOut) {
            $script:Aborted = $true
            throw "Command '$Name' did not finish within $TimeoutSeconds s; the remaining scenarios are skipped."
        }

        $result = @($shell.EndInvoke($async))[0]
        $shell.Dispose()
        $runspace.Dispose()
        if ($result.Ok) { break }
        if ($result.Error -match 'not available' -and $attempt -lt 20) {
            Start-Sleep -Seconds 3
            continue
        }
        throw "Command '$Name' failed: $($result.Error)"
    }
    Invoke-DialogPass
}

function Invoke-JanitorCommand {
    param([string]$Key, [string]$Arguments = '')
    $name = $script:Commands[$Key]
    if (-not $name) { throw "The command '$Key' is not registered in the Experimental instance." }
    Invoke-DteCommand $name $Arguments
    Wait-VsIdle
}

function Invoke-VsBuiltin {
    param([string]$Name)
    Com { $script:Dte.ExecuteCommand($Name, '') }
    Start-Sleep -Milliseconds 400
}

# ---------------------------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------------------------

function ConvertTo-Eol {
    param([string[]]$Lines, [string]$Eol = 'crlf')
    $builder = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        [void]$builder.Append($Lines[$i])
        $separator = switch ($Eol) {
            'lf' { "`n" }
            'mixed' { if ($i % 2 -eq 0) { "`r`n" } else { "`n" } }
            default { "`r`n" }
        }
        [void]$builder.Append($separator)
    }
    return $builder.ToString()
}

function Get-DirtyLines {
    param([string]$Namespace, [string]$Class, [string]$Marker = '', [switch]$UsingsInside)
    $classBlock = @(
        "    class $Class",
        '    {',
        '        int count;   ',
        '        List<int> numbers = new List<int>();',
        '',
        '',
        '        void Run(string text)',
        '        {',
        '            int value;',
        '            if (int.TryParse(text, out value))',
        '            {',
        '                count = value;',
        '            }',
        '            Dictionary<string, int> map = new Dictionary<string, int>();',
        '            int[] items = new int[] { 1, 2, 3 };',
        '            Func<int, int> twice = x => { return x * 2; };',
        '            Console.WriteLine(map.Count + items.Sum() + twice(count) + numbers.Count);',
        '        }'
    )
    if ($Marker) {
        $classBlock += @('', "        const int $Marker = 7;")
    }
    $classBlock += '    }'

    if ($UsingsInside) {
        return @("namespace $Namespace", '{', '    using System;', '    using System.Collections.Generic;', '    using System.Linq;', '') + $classBlock + @('}')
    }
    return @('using System.Linq;', 'using System;', 'using System.Collections.Generic;', '', "namespace $Namespace", '{') + $classBlock + @('}')
}

function Write-FixtureFile {
    param([string]$RelativePath, [string[]]$Lines, [string]$Eol = 'crlf', [switch]$Bom, [string]$Root = $script:Workspace)
    $path = Join-Path $Root $RelativePath
    $directory = Split-Path $path -Parent
    if (-not (Test-Path $directory)) { [void](New-Item -ItemType Directory -Path $directory) }
    if (Test-Path $path) { (Get-Item $path).IsReadOnly = $false }
    [IO.File]::WriteAllText($path, (ConvertTo-Eol $Lines $Eol), (New-Object System.Text.UTF8Encoding([bool]$Bom)))
    return $path
}

function Write-DirtyFixture {
    param([string]$RelativePath, [string]$Namespace, [string]$Class, [string]$Eol = 'crlf', [switch]$Bom, [switch]$UsingsInside, [string]$Marker = '', [string]$Root = $script:Workspace)
    $lines = Get-DirtyLines -Namespace $Namespace -Class $Class -Marker $Marker -UsingsInside:$UsingsInside
    return Write-FixtureFile -RelativePath $RelativePath -Lines $lines -Eol $Eol -Bom:$Bom -Root $Root
}

function Get-FixtureText {
    param([string]$Namespace, [string]$Class, [string]$Marker = '', [switch]$UsingsInside)
    return ConvertTo-Eol (Get-DirtyLines -Namespace $Namespace -Class $Class -Marker $Marker -UsingsInside:$UsingsInside) 'crlf'
}

function Get-CleanupDefects {
    param([string]$Text, [string]$Namespace, [string]$Class, [switch]$IgnoreUsingOrder)
    $defects = New-Object System.Collections.Generic.List[string]
    $escaped = [regex]::Escape($Namespace)
    if ($Text -match '^\s') { $defects.Add('begins with whitespace or a blank line') }
    if ($Text -notmatch "(?m)^namespace $escaped;\r?$") { $defects.Add('namespace is not file-scoped') }
    if ($Text -notmatch "internal (sealed )?class $Class\b") { $defects.Add('class has no explicit access modifier') }
    if ($Text -notmatch '\bvar map = ') { $defects.Add('explicit type kept where var is apparent') }
    if ($Text -notmatch '\[1, 2, 3\]') { $defects.Add('array initializer not converted to a collection expression') }
    if ($Text -notmatch 'x => x \* 2') { $defects.Add('block lambda not simplified') }
    if ($Text -notmatch 'out int value') { $defects.Add('out variable not inlined') }
    if ($Text -match '(?m)[ \t]+\r?$') { $defects.Add('trailing whitespace remains') }
    if ($Text -match '(\r?\n[ \t]*){3}') { $defects.Add('consecutive blank lines remain') }
    $system = $Text.IndexOf('using System;')
    $linq = $Text.IndexOf('using System.Linq;')
    $namespaceAt = $Text.IndexOf('namespace ')
    if (-not $IgnoreUsingOrder -and ($system -lt 0 -or $linq -lt 0 -or $system -gt $linq)) { $defects.Add('usings are not sorted') }
    if ($system -gt $namespaceAt) { $defects.Add('usings are still inside the namespace') }
    return @($defects)
}

function Test-Cleaned {
    param([string]$Name, [string]$Text, [string]$Namespace, [string]$Class, [switch]$IgnoreUsingOrder)
    $defects = Get-CleanupDefects $Text $Namespace $Class -IgnoreUsingOrder:$IgnoreUsingOrder
    Check $Name ($defects.Count -eq 0) ("{0}; text: {1}" -f ($defects -join ', '), (Show-Text $Text))
}

function Read-DiskText {
    param([string]$RelativePath, [string]$Root = $script:Workspace)
    return [IO.File]::ReadAllText((Join-Path $Root $RelativePath), (New-Object System.Text.UTF8Encoding($false)))
}

function Read-DiskBytes {
    param([string]$RelativePath, [string]$Root = $script:Workspace)
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $Root $RelativePath)))
}

function Get-DiskSnapshot {
    param([string[]]$RelativePaths)
    $snapshot = @{}
    foreach ($relative in $RelativePaths) { $snapshot[$relative] = Read-DiskBytes $relative }
    return $snapshot
}

function Test-SnapshotEqual {
    param([hashtable]$Before, [hashtable]$After)
    $changed = @()
    foreach ($key in $Before.Keys) {
        if ($Before[$key] -cne $After[$key]) { $changed += $key }
    }
    return $changed
}

function Invoke-Native {
    param([scriptblock]$Body)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Body 2>&1 | Out-String
        return @{ ExitCode = $LASTEXITCODE; Output = $output }
    }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Build {
    Wait-VsIdle
    $project = Join-Path $script:Workspace 'Sample\Sample.csproj'
    $result = Invoke-Native { dotnet build $project -nologo -v:q -nodeReuse:false -p:UseSharedCompilation=false }
    return @{ Ok = ($result.ExitCode -eq 0); Output = $result.Output }
}

function New-Workspace {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("cj-exp-it-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    [void](New-Item -ItemType Directory -Path $root)
    $script:Workspace = $root
    $script:SolutionPath = Join-Path $root 'Sample.sln'

    Write-FixtureFile 'Sample.sln' @(
        '',
        'Microsoft Visual Studio Solution File, Format Version 12.00',
        '# Visual Studio Version 17',
        'VisualStudioVersion = 17.0.31903.59',
        'MinimumVisualStudioVersion = 10.0.40219.1',
        "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Sample`", `"Sample\Sample.csproj`", `"$ProjectGuid`"",
        'EndProject',
        'Global',
        "`tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
        "`t`tDebug|Any CPU = Debug|Any CPU",
        "`t`tRelease|Any CPU = Release|Any CPU",
        "`tEndGlobalSection",
        "`tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
        "`t`t$ProjectGuid.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
        "`t`t$ProjectGuid.Debug|Any CPU.Build.0 = Debug|Any CPU",
        "`t`t$ProjectGuid.Release|Any CPU.ActiveCfg = Release|Any CPU",
        "`t`t$ProjectGuid.Release|Any CPU.Build.0 = Release|Any CPU",
        "`tEndGlobalSection",
        "`tGlobalSection(SolutionProperties) = preSolution",
        "`t`tHideSolutionNode = FALSE",
        "`tEndGlobalSection",
        'EndGlobal') | Out-Null

    Write-FixtureFile 'Sample\Sample.csproj' @(
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <PropertyGroup>',
        '    <TargetFramework>net8.0</TargetFramework>',
        '    <LangVersion>latest</LangVersion>',
        '    <Nullable>disable</Nullable>',
        '    <ImplicitUsings>disable</ImplicitUsings>',
        '    <RootNamespace>Sample</RootNamespace>',
        '  </PropertyGroup>',
        '</Project>') | Out-Null

    Write-FixtureFile '.editorconfig' @('root = true', '', '[*.cs]', 'indent_style = space', 'indent_size = 4') | Out-Null
    Write-FixtureFile '.gitignore' @('bin/', 'obj/') | Out-Null

    Write-FixtureFile '.codejanitor' @(
        '{',
        '  "insertExplicitAccessModifiers": true,',
        '  "convertToFileScopedNamespace": true,',
        '  "convertToVarWhenApparent": true,',
        '  "convertToCollectionExpressions": true,',
        '  "simplifySingleStatementLambdas": true,',
        '  "inlineOutVariableDeclarations": true,',
        '  "moveUsingsOutsideNamespace": true,',
        '  "organizeUsings": true,',
        '  "removeEndOfLineWhitespace": true,',
        '  "removeBlankLinesAtTop": true,',
        '  "removeMultipleConsecutiveBlankLines": true,',
        '  "removeByteOrderMark": false,',
        '  "removeRegions": false',
        '}') | Out-Null

    Write-FixtureFile 'CodeJanitor.config' @(
        '<?xml version="1.0" encoding="utf-8"?>',
        '<configuration>',
        '    <configSections>',
        '        <sectionGroup name="userSettings" type="System.Configuration.UserSettingsGroup, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089">',
        '            <section name="CodeJanitor.Properties.Settings" type="System.Configuration.ClientSettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" />',
        '        </sectionGroup>',
        '    </configSections>',
        '    <userSettings>',
        '        <CodeJanitor.Properties.Settings>',
        '            <setting name="Cleaning_AutoCleanupOnFileSave" serializeAs="String">',
        '                <value>False</value>',
        '            </setting>',
        '            <setting name="Cleaning_IncludeCSharp" serializeAs="String">',
        '                <value>True</value>',
        '            </setting>',
        '            <setting name="Cleaning_PerformPartialCleanupOnExternal" serializeAs="String">',
        '                <value>0</value>',
        '            </setting>',
        '            <setting name="Cleaning_ExclusionExpression" serializeAs="String">',
        '                <value />',
        '            </setting>',
        '            <setting name="Cleaning_RemoveByteOrderMark" serializeAs="String">',
        '                <value>False</value>',
        '            </setting>',
        '        </CodeJanitor.Properties.Settings>',
        '    </userSettings>',
        '</configuration>') | Out-Null

    Write-DirtyFixture 'Sample\Active\ActiveDocument.cs' 'Sample.Active' 'ActiveDocument' | Out-Null
    Write-DirtyFixture 'Sample\Leading\UsingsInside.cs' 'Sample.Leading' 'UsingsInside' -UsingsInside | Out-Null
    Write-DirtyFixture 'Sample\Open\Open1.cs' 'Sample.Open' 'Open1' | Out-Null
    Write-DirtyFixture 'Sample\Open\Open2.cs' 'Sample.Open' 'Open2' | Out-Null
    Write-DirtyFixture 'Sample\Open\NotOpen.cs' 'Sample.Open' 'NotOpen' | Out-Null
    Write-DirtyFixture 'Sample\Selected\SelA.cs' 'Sample.Selected' 'SelA' | Out-Null
    Write-DirtyFixture 'Sample\Selected\SelB.cs' 'Sample.Selected' 'SelB' | Out-Null
    Write-DirtyFixture 'Sample\All\CrLf.cs' 'Sample.All' 'AllCrLf' | Out-Null
    Write-DirtyFixture 'Sample\All\Lf.cs' 'Sample.All' 'AllLf' -Eol lf | Out-Null
    Write-DirtyFixture 'Sample\All\Mixed.cs' 'Sample.All' 'AllMixed' -Eol mixed | Out-Null
    Write-DirtyFixture 'Sample\All\WithBom.cs' 'Sample.All' 'AllBom' -Bom | Out-Null
    Write-DirtyFixture 'Sample\All\ClosedUsingsInside.cs' 'Sample.All' 'AllUsingsInside' -UsingsInside | Out-Null
    Write-DirtyFixture 'Sample\Changed\Changed.cs' 'Sample.Changed' 'Changed' | Out-Null
    Write-DirtyFixture 'Sample\Changed\Committed.cs' 'Sample.Changed' 'Committed' | Out-Null
    Write-DirtyFixture 'Sample\Save\SaveToggle.cs' 'Sample.Save' 'SaveToggle' | Out-Null
    Write-DirtyFixture 'Sample\Undo\UndoDocument.cs' 'Sample.Undo' 'UndoDocument' | Out-Null
    Write-DirtyFixture 'Sample\ReadOnly\Locked.cs' 'Sample.ReadOnly' 'Locked' | Out-Null
    Write-DirtyFixture 'Sample\ReadOnly\Sibling.cs' 'Sample.ReadOnly' 'Sibling' | Out-Null

    Write-FixtureFile 'Sample\Ns\Misplaced.cs' @('namespace Sample.NotTheFolder;', '', 'internal class Misplaced', '{', '}') | Out-Null
    Write-FixtureFile 'Sample\Ns\MisplacedClosed.cs' @('namespace Sample.NotTheFolder;', '', 'internal class MisplacedClosed', '{', '}') | Out-Null
    Write-FixtureFile 'Sample\Regions\Regions.cs' (Get-RegionLines) | Out-Null
    Write-FixtureFile 'Sample\Reorg\Reorg.cs' (Get-ReorganizeLines) | Out-Null
    Write-FixtureFile 'Sample\Lines\Lines.cs' (Get-LinesFixture) | Out-Null

    Write-DirtyFixture 'External.cs' 'External.Outside' 'ExternalFile' -Root (Join-Path $root 'ext') | Out-Null
    Write-FixtureFile '.codejanitor' (Get-Content (Join-Path $root '.codejanitor')) -Root (Join-Path $root 'ext') | Out-Null

    $script:HasGit = [bool](Get-Command git -ErrorAction SilentlyContinue)
    if ($script:HasGit) {
        Push-Location $root
        try {
            $null = Invoke-Native { git init -q }
            $null = Invoke-Native { git config user.email 'cj-exp@example.invalid' }
            $null = Invoke-Native { git config user.name 'cj-exp' }
            $null = Invoke-Native { git config core.autocrlf false }
            $null = Invoke-Native { git add -A }
            $null = Invoke-Native { git commit -q -m 'fixtures' }
        }
        finally { Pop-Location }
    }
}

function Get-RegionLines {
    return @(
        'using System;',
        '',
        'namespace Sample.Regions',
        '{',
        '    internal class RegionHolder',
        '    {',
        '        #region Fields',
        '',
        '        private int value;',
        '',
        '        #endregion',
        '',
        '        #region Methods',
        '',
        '        public int Get()',
        '        {',
        '            return value;',
        '        }',
        '',
        '        #endregion',
        '    }',
        '}')
}

function Get-ReorganizeLines {
    return @(
        'using System;',
        '',
        'namespace Sample.Reorg',
        '{',
        '    internal class Reordered',
        '    {',
        '        public void Method()',
        '        {',
        '            Console.WriteLine(Value);',
        '        }',
        '',
        '        public int Value { get; set; }',
        '',
        '        private int field;',
        '',
        '        public Reordered()',
        '        {',
        '        }',
        '    }',
        '}')
}

function Get-LinesFixture {
    return @(
        'namespace Sample.Lines',
        '{',
        '    // charlie',
        '    // alpha',
        '    // bravo',
        '    // join-one',
        '    // join-two',
        '    internal class LineHolder',
        '    {',
        '    }',
        '}')
}

# ---------------------------------------------------------------------------------------------
# Visual Studio lifecycle
# ---------------------------------------------------------------------------------------------

function Find-Devenv {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = & $vswhere -latest -prerelease -property installationPath
    if (-not $installation) { throw 'Visual Studio was not found by vswhere.' }
    return Join-Path $installation 'Common7\IDE\devenv.exe'
}

function Start-ExpVs {
    $devenv = Find-Devenv
    $before = @(Get-Process devenv -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    Write-Host "Starting Experimental instance on $script:SolutionPath"
    $null = Start-Process -FilePath $devenv -ArgumentList "/rootsuffix Exp `"$script:SolutionPath`"" -PassThru

    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and $script:VsPid -le 0) {
        Start-Sleep -Seconds 1
        $started = Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -match '(?i)/rootsuffix\s+Exp' -and $_.CommandLine.Contains($script:SolutionPath) -and ($before -notcontains [int]$_.ProcessId) }
        $first = @($started) | Select-Object -First 1
        if ($first) { $script:VsPid = [int]$first.ProcessId }
    }
    if ($script:VsPid -le 0) { throw 'The Experimental devenv.exe process was not found after launch.' }
    Write-Host "Experimental devenv PID $script:VsPid"

    [CjWatchdog]::Start($script:VsPid, $TotalTimeoutMinutes * 60000)
    $script:UserAutoSaveAtStart = Get-UserAutoSaveSetting
}

function Connect-Dte {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $script:VsPid -ErrorAction SilentlyContinue)) { throw 'The Experimental instance exited during startup.' }
        foreach ($version in '18.0', '17.0') {
            $dte = [Rot]::Find("!VisualStudio.DTE.${version}:$($script:VsPid)")
            if ($dte) { $script:Dte = $dte; break }
        }
        if ($script:Dte) {
            try {
                $script:MainWindow = [IntPtr](Get-Process -Id $script:VsPid).MainWindowHandle
                $probe = Join-Path $script:Workspace 'Sample\Active\ActiveDocument.cs'
                if ($script:Dte.Solution.IsOpen -and $script:Dte.Solution.FindProjectItem($probe)) { return }
            }
            catch { }
        }
        Invoke-DialogPass
        Start-Sleep -Seconds 2
    }
    throw "The solution did not finish loading in the Experimental instance within $StartupTimeoutSeconds s."
}

function Resolve-Commands {
    $patterns = [ordered]@{
        ActiveDocument = '^CodeJanitor\.Cleanup\s*Active'
        AllCode        = '^CodeJanitor\.Cleanup\s*All'
        OpenCode       = '^CodeJanitor\.Cleanup\s*Open'
        SelectedCode   = '^(ProjectandSolutionContextMenus\.Project\.)?CodeJanitor\.Cleanup\s*Selected'
        ChangedFiles   = '^CodeJanitor\.Cleanup\s*Changed'
        FixNamespace   = '^CodeJanitor\.Fix\s*Namespace'
        RemoveRegion   = '^CodeJanitor\.Remove\s*(All\s*)?Region'
        Reorganize     = '^CodeJanitor\.Reorgani[sz]e'
        SortLines      = '^CodeJanitor\.Sort\s*Lines'
        JoinLines      = '^CodeJanitor\.Join\s*Lines'
        Options        = '^CodeJanitor\.Options$'
        CleanupOnSave  = '^CodeJanitor\..*(Cleanup\s*On\s*Save|Automatic)'
    }
    $deadline = (Get-Date).AddSeconds(120)
    $names = @()
    do {
        $names = @(Com {
                $found = @()
                foreach ($command in $script:Dte.Commands) {
                    $name = $command.Name
                    if ($name -and ($name -like 'CodeJanitor*' -or $name -like '*.CodeJanitor.*')) { $found += $name }
                }
                $found
            })
        if ($names.Count -eq 0) { Start-Sleep -Seconds 5 }
    } while ($names.Count -eq 0 -and (Get-Date) -lt $deadline)

    foreach ($key in $patterns.Keys) {
        $match = @($names | Where-Object { $_ -match $patterns[$key] }) | Select-Object -First 1
        if ($match) { $script:Commands[$key] = $match }
    }
    Write-Info 'CodeJanitor commands:'
    for ($i = 0; $i -lt $names.Count; $i += 4) {
        Write-Info ('  ' + (@($names | Select-Object -Skip $i -First 4) -join ', '))
    }
    return $names
}

function Stop-ExpVs {
    [CjWatchdog]::Stop()
    if ($script:VsPid -gt 0) {
        $process = Get-Process -Id $script:VsPid -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $script:VsPid -Force -ErrorAction SilentlyContinue
            $null = $process.WaitForExit(30000)
        }
    }
}

function Remove-Workspace {
    if (-not $script:Workspace -or -not (Test-Path $script:Workspace)) { return }
    if ($KeepWorkspace) {
        Write-Host "Workspace kept: $script:Workspace"
        return
    }
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Get-ChildItem -LiteralPath $script:Workspace -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if (-not $_.PSIsContainer -and $_.IsReadOnly) { $_.IsReadOnly = $false }
            }
            Remove-Item -LiteralPath $script:Workspace -Recurse -Force -ErrorAction Stop
            return
        }
        catch { Start-Sleep -Seconds 2 }
    }
    Write-Host "Could not delete $script:Workspace" -ForegroundColor Yellow
}

function Get-UserAutoSaveSetting {
    if (-not (Test-Path $script:UserConfigPath)) { return '<absent>' }
    try {
        [xml]$config = Get-Content -LiteralPath $script:UserConfigPath -Raw
        $setting = $config.configuration.userSettings.'CodeJanitor.Properties.Settings'.setting | Where-Object { $_.name -eq 'Cleaning_AutoCleanupOnFileSave' }
        if ($setting) { return [string]$setting.value }
        return '<unset>'
    }
    catch { return '<unreadable>' }
}

# ---------------------------------------------------------------------------------------------
# Scenarios
# ---------------------------------------------------------------------------------------------

function Reset-Scenario {
    param([object[]]$Rules = @())
    Set-DialogRules $Rules
    $script:Unexpected.Clear()
    Close-AllDocuments
}

$scenarios = [ordered]@{}

$scenarios['Startup'] = {
    Reset-Scenario
    $expected = 'ActiveDocument', 'AllCode', 'OpenCode', 'SelectedCode', 'ChangedFiles', 'FixNamespace', 'RemoveRegion', 'Reorganize', 'SortLines', 'JoinLines', 'Options', 'CleanupOnSave'
    foreach ($key in $expected) {
        Check "command '$key' is registered" ([bool]$script:Commands[$key]) "no CodeJanitor command matches; registered: $($script:RegisteredNames -join ', ')"
    }
}

$scenarios['ActiveDocument'] = {
    Reset-Scenario
    $relative = 'Sample\Active\ActiveDocument.cs'
    $path = Write-DirtyFixture $relative 'Sample.Active' 'ActiveDocument'
    $diskBefore = Read-DiskBytes $relative
    $null = Open-ProjectFile $path
    Set-BufferText $path (Get-FixtureText 'Sample.Active' 'ActiveDocument' -Marker 'EditedInBuffer')
    $dirtyBuffer = Get-BufferText $path

    Invoke-JanitorCommand ActiveDocument
    $cleaned = Get-BufferText $path
    Test-Cleaned 'edited unsaved buffer is cleaned' $cleaned 'Sample.Active' 'ActiveDocument'
    Check 'unsaved edit survives the cleanup' ($cleaned -match '\bEditedInBuffer\b') (Show-Text $cleaned)
    Check 'cleanup changed the buffer' ($cleaned -cne $dirtyBuffer)
    Check 'cleanup does not save the document' ((Read-DiskBytes $relative) -ceq $diskBefore) 'file on disk changed'

    Invoke-JanitorCommand ActiveDocument
    $second = Get-BufferText $path
    Check 'second cleanup changes nothing (idempotent)' ($second -ceq $cleaned) ("first: {0} second: {1}" -f (Show-Text $cleaned 200), (Show-Text $second 200))
}

$scenarios['LeadingBlankLine'] = {
    Reset-Scenario
    $relative = 'Sample\Leading\UsingsInside.cs'
    $path = Write-DirtyFixture $relative 'Sample.Leading' 'UsingsInside' -UsingsInside
    $null = Open-ProjectFile $path

    Invoke-JanitorCommand ActiveDocument
    $first = Get-BufferText $path
    Check 'first cleanup of usings inside a namespace leaves no leading blank line' ($first -notmatch '^\s') (Show-Text $first 120)
    Test-Cleaned 'usings inside a namespace are fully cleaned in one pass' $first 'Sample.Leading' 'UsingsInside'

    Invoke-JanitorCommand ActiveDocument
    $second = Get-BufferText $path
    Check 'second cleanup changes nothing (idempotent)' ($second -ceq $first) ("first: {0} second: {1}" -f (Show-Text $first 120), (Show-Text $second 120))
}

$scenarios['OpenCode'] = {
    Reset-Scenario
    $open1 = Write-DirtyFixture 'Sample\Open\Open1.cs' 'Sample.Open' 'Open1'
    $open2 = Write-DirtyFixture 'Sample\Open\Open2.cs' 'Sample.Open' 'Open2'
    $notOpen = Write-DirtyFixture 'Sample\Open\NotOpen.cs' 'Sample.Open' 'NotOpen'
    $notOpenBefore = Read-DiskBytes 'Sample\Open\NotOpen.cs'
    $null = Open-ProjectFile $open1
    $null = Open-ProjectFile $open2
    Set-BufferText $open1 (Get-FixtureText 'Sample.Open' 'Open1' -Marker 'EditedOpen1')

    Invoke-JanitorCommand OpenCode
    $buffer1 = Get-BufferText $open1
    $buffer2 = Get-BufferText $open2
    Test-Cleaned 'first open document is cleaned' $buffer1 'Sample.Open' 'Open1'
    Test-Cleaned 'second open document is cleaned' $buffer2 'Sample.Open' 'Open2'
    Check 'unsaved edit of the first document survives' ($buffer1 -match '\bEditedOpen1\b') (Show-Text $buffer1)
    Check 'a file that is not open is left untouched' ((Read-DiskBytes 'Sample\Open\NotOpen.cs') -ceq $notOpenBefore) 'NotOpen.cs changed'

    Com { $script:Dte.Documents.SaveAll() }
    Test-Cleaned 'saved file on disk is cleaned' (Read-DiskText 'Sample\Open\Open2.cs') 'Sample.Open' 'Open2'

    Invoke-JanitorCommand OpenCode
    Check 'second run changes no buffer (idempotent)' (((Get-BufferText $open1) -ceq $buffer1) -and ((Get-BufferText $open2) -ceq $buffer2))
}

$scenarios['SelectedScope'] = {
    $rules = @(
        (New-DialogRule '^Code Janitor Cleanup Options$' '^Start Cleanup$')
    )
    Reset-Scenario $rules
    $selA = Write-DirtyFixture 'Sample\Selected\SelA.cs' 'Sample.Selected' 'SelA'
    $selB = Write-DirtyFixture 'Sample\Selected\SelB.cs' 'Sample.Selected' 'SelB'
    $selBBefore = Read-DiskBytes 'Sample\Selected\SelB.cs'

    Select-SolutionItem @('Sample', 'Selected', 'SelA.cs')
    Invoke-JanitorCommand SelectedCode
    Check 'cleanup options dialog was answered' (Test-DialogSeen '^Code Janitor Cleanup Options$')
    Test-Cleaned 'selected file is cleaned on disk' (Read-DiskText 'Sample\Selected\SelA.cs') 'Sample.Selected' 'SelA'
    Check 'unselected sibling is left untouched' ((Read-DiskBytes 'Sample\Selected\SelB.cs') -ceq $selBBefore) 'SelB.cs changed'

    $selAAfter = Read-DiskBytes 'Sample\Selected\SelA.cs'
    Select-SolutionItem @('Sample', 'Selected', 'SelA.cs')
    Invoke-JanitorCommand SelectedCode
    Check 'second run changes nothing (idempotent)' ((Read-DiskBytes 'Sample\Selected\SelA.cs') -ceq $selAAfter)

    Select-SolutionItem @('Sample', 'Selected')
    Invoke-JanitorCommand SelectedCode
    Test-Cleaned 'selecting the folder cleans the remaining file' (Read-DiskText 'Sample\Selected\SelB.cs') 'Sample.Selected' 'SelB'

    Set-DialogRules @((New-DialogRule '^Code Janitor Cleanup Options$' '^Cancel$'))
    Write-DirtyFixture 'Sample\Selected\SelB.cs' 'Sample.Selected' 'SelB' | Out-Null
    $selBDirty = Read-DiskBytes 'Sample\Selected\SelB.cs'
    Select-SolutionItem @('Sample', 'Selected', 'SelB.cs')
    Invoke-JanitorCommand SelectedCode
    Check 'cancelling the options dialog leaves the file untouched' ((Read-DiskBytes 'Sample\Selected\SelB.cs') -ceq $selBDirty)
}

$scenarios['CleanupAllCode'] = {
    $confirm = '^CodeJanitor: Confirmation for Cleanup All Code$'
    Reset-Scenario @((New-DialogRule $confirm '^No$'))
    $all = 'Sample\All\CrLf.cs', 'Sample\All\Lf.cs', 'Sample\All\Mixed.cs', 'Sample\All\WithBom.cs', 'Sample\All\ClosedUsingsInside.cs'
    Write-DirtyFixture 'Sample\All\CrLf.cs' 'Sample.All' 'AllCrLf' | Out-Null
    Write-DirtyFixture 'Sample\All\Lf.cs' 'Sample.All' 'AllLf' -Eol lf | Out-Null
    Write-DirtyFixture 'Sample\All\Mixed.cs' 'Sample.All' 'AllMixed' -Eol mixed | Out-Null
    Write-DirtyFixture 'Sample\All\WithBom.cs' 'Sample.All' 'AllBom' -Bom | Out-Null
    Write-DirtyFixture 'Sample\All\ClosedUsingsInside.cs' 'Sample.All' 'AllUsingsInside' -UsingsInside | Out-Null
    $dirty = Get-DiskSnapshot $all

    Invoke-JanitorCommand AllCode
    Check 'declining the confirmation changes no file' ((Test-SnapshotEqual $dirty (Get-DiskSnapshot $all)).Count -eq 0) ((Test-SnapshotEqual $dirty (Get-DiskSnapshot $all)) -join ', ')

    Set-DialogRules @((New-DialogRule $confirm '^Yes$'))
    Invoke-JanitorCommand AllCode
    Check 'confirmation dialog was answered' (Test-DialogSeen $confirm)
    Test-Cleaned 'CRLF file is cleaned' (Read-DiskText 'Sample\All\CrLf.cs') 'Sample.All' 'AllCrLf'
    Test-Cleaned 'LF file is cleaned' (Read-DiskText 'Sample\All\Lf.cs') 'Sample.All' 'AllLf'
    Test-Cleaned 'mixed line ending file is cleaned' (Read-DiskText 'Sample\All\Mixed.cs') 'Sample.All' 'AllMixed'
    Test-Cleaned 'file with byte order mark is cleaned' (Read-DiskText 'Sample\All\WithBom.cs') 'Sample.All' 'AllBom'

    $closedUsings = Read-DiskText 'Sample\All\ClosedUsingsInside.cs'
    Check 'closed file with usings inside a namespace has no leading blank line after one pass' ($closedUsings -notmatch '^\s') (Show-Text $closedUsings 120)
    Test-Cleaned 'closed file with usings inside a namespace is fully cleaned in one pass' $closedUsings 'Sample.All' 'AllUsingsInside'

    $crlf = Read-DiskText 'Sample\All\CrLf.cs'
    $lf = Read-DiskText 'Sample\All\Lf.cs'
    $mixed = Read-DiskText 'Sample\All\Mixed.cs'
    Check 'CRLF file keeps only CRLF line endings' ($crlf -notmatch '(?<!\r)\n')
    Check 'LF file keeps only LF line endings' ($lf -notmatch '\r')
    Check 'mixed line endings are preserved' (($mixed -match '\r\n') -and ($mixed -match '(?<!\r)\n')) (Show-Text $mixed 200)
    $bomBytes = [IO.File]::ReadAllBytes((Join-Path $script:Workspace 'Sample\All\WithBom.cs'))
    Check 'byte order mark is preserved' ($bomBytes.Length -ge 3 -and $bomBytes[0] -eq 0xEF -and $bomBytes[1] -eq 0xBB -and $bomBytes[2] -eq 0xBF)
    $noBomBytes = [IO.File]::ReadAllBytes((Join-Path $script:Workspace 'Sample\All\CrLf.cs'))
    Check 'a file without byte order mark gets none' (-not ($noBomBytes.Length -ge 3 -and $noBomBytes[0] -eq 0xEF -and $noBomBytes[1] -eq 0xBB -and $noBomBytes[2] -eq 0xBF))

    $cleaned = Get-DiskSnapshot $all
    Invoke-JanitorCommand AllCode
    $changed = Test-SnapshotEqual $cleaned (Get-DiskSnapshot $all)
    Check 'second run changes no file (idempotent)' ($changed.Count -eq 0) ($changed -join ', ')
}

$scenarios['ChangedFiles'] = {
    $none = '^CodeJanitor: Cleanup Changed Files$'
    $confirm = '^CodeJanitor: Confirmation for Cleanup Changed Files$'
    Reset-Scenario @((New-DialogRule $none '^OK$'), (New-DialogRule $confirm '^Yes$'))
    if (-not $script:HasGit) { Check 'git is available' $false 'git was not found on PATH'; return }

    Write-DirtyFixture 'Sample\Changed\Committed.cs' 'Sample.Changed' 'Committed' | Out-Null
    Write-DirtyFixture 'Sample\Changed\Changed.cs' 'Sample.Changed' 'Changed' | Out-Null
    Push-Location $script:Workspace
    try {
        $null = Invoke-Native { git add -A }
        $null = Invoke-Native { git commit -q -m 'sync' }
    }
    finally { Pop-Location }
    $committed = Read-DiskBytes 'Sample\Changed\Committed.cs'
    $everything = @('Sample\Changed\Committed.cs', 'Sample\Changed\Changed.cs', 'Sample\All\CrLf.cs', 'Sample\Open\NotOpen.cs')
    $clean = Get-DiskSnapshot $everything

    Invoke-JanitorCommand ChangedFiles
    Check 'clean working tree reports that no file changed' (Test-DialogSeen $none)
    Check 'clean working tree cleans nothing' ((Test-SnapshotEqual $clean (Get-DiskSnapshot $everything)).Count -eq 0)

    Write-FixtureFile 'Sample\Changed\Changed.cs' ((Get-DirtyLines 'Sample.Changed' 'Changed' -Marker 'ChangedMarker')) | Out-Null
    $dirtyChanged = Read-DiskBytes 'Sample\Changed\Changed.cs'
    Invoke-JanitorCommand ChangedFiles
    Check 'confirmation dialog was answered' (Test-DialogSeen $confirm)
    $cleaned = Read-DiskText 'Sample\Changed\Changed.cs'
    Test-Cleaned 'the file modified in git is cleaned' $cleaned 'Sample.Changed' 'Changed'
    Check 'the cleaned file kept its content' ($cleaned -match '\bChangedMarker\b' -and (Read-DiskBytes 'Sample\Changed\Changed.cs') -cne $dirtyChanged)
    Check 'a committed unmodified file is left untouched' ((Read-DiskBytes 'Sample\Changed\Committed.cs') -ceq $committed)

    $afterFirst = Read-DiskBytes 'Sample\Changed\Changed.cs'
    Invoke-JanitorCommand ChangedFiles
    Check 'second run changes nothing (idempotent)' ((Read-DiskBytes 'Sample\Changed\Changed.cs') -ceq $afterFirst)
}

$scenarios['AutoCleanupOnSave'] = {
    Reset-Scenario
    $relative = 'Sample\Save\SaveToggle.cs'
    $path = Write-DirtyFixture $relative 'Sample.Save' 'SaveToggle'
    $null = Open-ProjectFile $path

    Invoke-JanitorCommand CleanupOnSave
    $probe = Save-WithMarker $path $relative 'SavedWithCleanup'
    if (-not $probe.Cleaned) {
        Invoke-JanitorCommand CleanupOnSave
        $probe = Save-WithMarker $path $relative 'SavedWithCleanup'
    }
    Check 'toggle turns automatic cleanup on save ON: saving cleans the file' $probe.Cleaned (Show-Text $probe.Disk 300)
    Test-Cleaned 'the file saved with the toggle ON is cleaned on disk' $probe.Disk 'Sample.Save' 'SaveToggle' -IgnoreUsingOrder
    Check 'saved file kept its content' ($probe.Disk -match '\bSavedWithCleanup\b')

    Invoke-JanitorCommand CleanupOnSave
    $probe = Save-WithMarker $path $relative 'SavedWithoutCleanup'
    $typed = Get-FixtureText 'Sample.Save' 'SaveToggle' -Marker 'SavedWithoutCleanup'
    Check 'toggle turns automatic cleanup on save OFF: saving leaves the file as typed' (($probe.Disk -replace "`r`n", "`n") -ceq ($typed -replace "`r`n", "`n")) (Show-Text $probe.Disk 300)
}

function Save-WithMarker {
    param([string]$Path, [string]$Relative, [string]$Marker)
    Set-BufferText $Path (Get-FixtureText 'Sample.Save' 'SaveToggle' -Marker $Marker)
    Com { (Get-VsDocument $Path).Save() }
    Invoke-DialogPass
    $disk = Read-DiskText $Relative
    return @{ Disk = $disk; Cleaned = ($disk -match '(?m)^namespace Sample\.Save;') }
}

$scenarios['FixNamespace'] = {
    $confirm = '^CodeJanitor Fix Namespace Confirmation$'
    $done = '^CodeJanitor Fix Namespace$'
    Reset-Scenario @((New-DialogRule $confirm '^Yes$'), (New-DialogRule $done '^OK$'))
    $open = Write-FixtureFile 'Sample\Ns\Misplaced.cs' @('namespace Sample.NotTheFolder;', '', 'internal class Misplaced', '{', '}')
    $closed = Write-FixtureFile 'Sample\Ns\MisplacedClosed.cs' @('namespace Sample.NotTheFolder;', '', 'internal class MisplacedClosed', '{', '}')
    $null = Open-ProjectFile $open

    Invoke-JanitorCommand FixNamespace
    Check 'confirmation and summary dialogs were answered' ((Test-DialogSeen $confirm) -and (Test-DialogSeen $done))
    $buffer = Get-BufferText $open
    Check 'open document gets the namespace of its folder' ($buffer -match '(?m)^namespace Sample\.Ns;') (Show-Text $buffer)

    Invoke-JanitorCommand FixNamespace
    Check 'second run changes nothing (idempotent)' ((Get-BufferText $open) -ceq $buffer)

    Close-AllDocuments
    Select-SolutionItem @('Sample', 'Ns', 'MisplacedClosed.cs')
    Invoke-JanitorCommand FixNamespace
    $disk = Read-DiskText 'Sample\Ns\MisplacedClosed.cs'
    Check 'closed selected file gets the namespace of its folder' ($disk -match '(?m)^namespace Sample\.Ns;') (Show-Text $disk)
}

$scenarios['RemoveAllRegions'] = {
    Reset-Scenario
    $path = Write-FixtureFile 'Sample\Regions\Regions.cs' (Get-RegionLines)
    $null = Open-ProjectFile $path
    Set-Selection $path 1

    Invoke-JanitorCommand RemoveRegion
    $buffer = Get-BufferText $path
    Check 'all region directives are removed' ($buffer -notmatch '#region|#endregion') (Show-Text $buffer)
    Check 'code between the regions is kept' (($buffer -match 'private int value;') -and ($buffer -match 'public int Get\(\)') -and ($buffer -match 'return value;')) (Show-Text $buffer)

    Invoke-JanitorCommand RemoveRegion
    Check 'second run changes nothing (idempotent)' ((Get-BufferText $path) -ceq $buffer)
}

$scenarios['Reorganize'] = {
    Reset-Scenario
    $path = Write-FixtureFile 'Sample\Reorg\Reorg.cs' (Get-ReorganizeLines)
    $null = Open-ProjectFile $path

    Invoke-JanitorCommand Reorganize
    $buffer = Get-BufferText $path
    $field = $buffer.IndexOf('int field;')
    $constructor = $buffer.IndexOf('public Reordered()')
    $property = $buffer.IndexOf('public int Value')
    $method = $buffer.IndexOf('public void Method()')
    Check 'members are reordered fields, constructors, properties, methods' (($field -ge 0) -and ($field -lt $constructor) -and ($constructor -lt $property) -and ($property -lt $method)) (Show-Text $buffer 600)

    Invoke-JanitorCommand Reorganize
    Check 'second run changes nothing (idempotent)' ((Get-BufferText $path) -ceq $buffer)
}

$scenarios['SortAndJoinLines'] = {
    Reset-Scenario
    $path = Write-FixtureFile 'Sample\Lines\Lines.cs' (Get-LinesFixture)
    $null = Open-ProjectFile $path

    Set-Selection $path 3 6
    Invoke-JanitorCommand SortLines
    $buffer = Get-BufferText $path
    $alpha = $buffer.IndexOf('// alpha')
    $bravo = $buffer.IndexOf('// bravo')
    $charlie = $buffer.IndexOf('// charlie')
    Check 'selected lines are sorted' (($alpha -ge 0) -and ($alpha -lt $bravo) -and ($bravo -lt $charlie)) (Show-Text $buffer 300)

    $lineNumber = (($buffer -split "`r?`n") | ForEach-Object { $_ } | Select-String -SimpleMatch '// join-one').LineNumber
    Set-Selection $path $lineNumber ($lineNumber + 1) -ToEndOfLine
    Invoke-JanitorCommand JoinLines
    $buffer = Get-BufferText $path
    Check 'selected lines are joined into one line' ($buffer -match '// join-one // join-two') (Show-Text $buffer 300)
}

$scenarios['Options'] = {
    Reset-Scenario @((New-DialogRule '^(Options|Opcje)$' '^(Cancel|Anuluj)$'))
    Invoke-JanitorCommand Options
    Check 'the Options dialog opened and was closed' (Test-DialogSeen '^(Options|Opcje)$') 'no top-level window titled Options appeared'
}

$scenarios['ExternalFile'] = {
    $prompt = '^CodeJanitor: Cleanup External File$'
    Reset-Scenario @((New-DialogRule $prompt '^Yes$'))
    $path = Join-Path $script:Workspace 'ext\External.cs'
    Write-DirtyFixture 'External.cs' 'External.Outside' 'ExternalFile' -Root (Join-Path $script:Workspace 'ext') | Out-Null
    $null = Open-ExternalFile $path
    $before = Get-BufferText $path

    Invoke-JanitorCommand ActiveDocument
    Check 'the external file prompt appeared and was answered Yes' (Test-DialogSeen $prompt)
    $after = Get-BufferText $path
    Check 'partial cleanup of an external file changed the buffer' ($after -cne $before)
    Check 'partial cleanup removed trailing whitespace' ($after -notmatch '(?m)[ \t]+\r?$') (Show-Text $after)
    Check 'partial cleanup removed consecutive blank lines' ($after -notmatch '(\r?\n[ \t]*){3}') (Show-Text $after)

    Close-AllDocuments
    Set-DialogRules @((New-DialogRule $prompt '^No$'))
    Write-DirtyFixture 'External.cs' 'External.Outside' 'ExternalFile' -Root (Join-Path $script:Workspace 'ext') | Out-Null
    $null = Open-ExternalFile $path
    $before = Get-BufferText $path
    Invoke-JanitorCommand ActiveDocument
    Check 'the external file prompt appeared and was answered No' (Test-DialogSeen $prompt)
    Check 'answering No leaves the external file unchanged' ((Get-BufferText $path) -ceq $before)
}

$scenarios['ReadOnly'] = {
    $confirm = '^CodeJanitor: Confirmation for Cleanup All Code$'
    Reset-Scenario @((New-DialogRule $confirm '^Yes$'), (New-DialogRule '^CodeJanitor Cleanup Warning$' '^OK$'), (New-DialogRule '^Microsoft Visual Studio$' '^(Cancel|Anuluj)$'))
    $lockedPath = Write-DirtyFixture 'Sample\ReadOnly\Locked.cs' 'Sample.ReadOnly' 'Locked'
    Write-DirtyFixture 'Sample\ReadOnly\Sibling.cs' 'Sample.ReadOnly' 'Sibling' | Out-Null
    $lockedBefore = Read-DiskBytes 'Sample\ReadOnly\Locked.cs'
    (Get-Item $lockedPath).IsReadOnly = $true
    try {
        $null = Open-ProjectFile $lockedPath
        Invoke-JanitorCommand ActiveDocument
        Check 'cleaning a read-only open document leaves the file on disk unchanged' ((Read-DiskBytes 'Sample\ReadOnly\Locked.cs') -ceq $lockedBefore)
        Check 'the read-only attribute is kept' ((Get-Item $lockedPath).IsReadOnly)
        Close-AllDocuments

        Invoke-JanitorCommand AllCode
        Test-Cleaned 'a writable sibling is cleaned while a read-only file is present' (Read-DiskText 'Sample\ReadOnly\Sibling.cs') 'Sample.ReadOnly' 'Sibling'
        Check 'the closed read-only file is left unchanged' ((Read-DiskBytes 'Sample\ReadOnly\Locked.cs') -ceq $lockedBefore)
        Check 'the closed read-only file is still read-only' ((Get-Item $lockedPath).IsReadOnly)
    }
    finally {
        (Get-Item $lockedPath).IsReadOnly = $false
    }
}

$scenarios['Undo'] = {
    Reset-Scenario
    $path = Write-DirtyFixture 'Sample\Undo\UndoDocument.cs' 'Sample.Undo' 'UndoDocument'
    $null = Open-ProjectFile $path
    Set-BufferText $path (Get-FixtureText 'Sample.Undo' 'UndoDocument' -Marker 'UndoMarker')
    $original = Get-BufferText $path

    Invoke-JanitorCommand ActiveDocument
    $cleaned = Get-BufferText $path
    Check 'cleanup changed the buffer' ($cleaned -cne $original)

    $undos = 0
    $current = $cleaned
    while ($current -cne $original -and $undos -lt 4) {
        $null = Com { (Get-VsDocument $path).Undo() }
        Start-Sleep -Milliseconds 400
        $undos++
        $next = Get-BufferText $path
        if ($next -ceq $current) { break }
        $current = $next
    }
    Check 'undo restores the text before the cleanup' ($current -ceq $original) ("after $undos undo(s): " + (Show-Text $current 300))
    Check 'one cleanup takes at most two undo steps (Janitor cleanup and diagnostic cleanup)' ($undos -ge 1 -and $undos -le 2) "$undos undo step(s)"

    for ($i = 0; $i -lt $undos; $i++) { $null = Com { (Get-VsDocument $path).Redo() }; Start-Sleep -Milliseconds 400 }
    Check 'redo reapplies the cleanup' ((Get-BufferText $path) -ceq $cleaned)
}

$scenarios['Build'] = {
    Reset-Scenario
    $build = Invoke-Build
    Check 'the cleaned solution still builds' $build.Ok (Show-Text $build.Output 800)
}

$scenarios['OutputPane'] = {
    Reset-Scenario
    $text = Com {
        $collected = ''
        foreach ($pane in $script:Dte.ToolWindows.OutputWindow.OutputWindowPanes) {
            if ($pane.Name -match 'Janitor') {
                $document = $pane.TextDocument
                $collected += $document.StartPoint.CreateEditPoint().GetText($document.EndPoint)
            }
        }
        $collected
    }
    Check 'the CodeJanitor output pane has content' (-not [string]::IsNullOrWhiteSpace($text))
    $errors = @(($text -split "`r?`n") | Where-Object { $_ -match 'Exception|Unable to|failed' })
    Check 'the CodeJanitor output pane logged no exception or failure' ($errors.Count -eq 0) (Show-Text ($errors -join ' | ') 800)
}

$scenarios['UserSettings'] = {
    Reset-Scenario
    $now = Get-UserAutoSaveSetting
    Check "the user's CodeJanitor.config was not changed by the toggle" ($now -ceq $script:UserAutoSaveAtStart) "before: $($script:UserAutoSaveAtStart) after: $now"
}

# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------

$exitCode = 1
try {
    if (-not $SkipDeploy) {
        Write-Host "Deploying the VSIX ($Configuration) to the Experimental hive..."
        Use-BuildMutex {
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $deployScript -Configuration $Configuration
                $deployExit = $LASTEXITCODE
            }
            finally { $ErrorActionPreference = $previous }
            if ($deployExit -ne 0) { throw "deploy-exp.ps1 failed with exit code $deployExit." }
        }
    }

    Write-Host 'Creating the temporary workspace...'
    New-Workspace
    Write-Host "Workspace: $script:Workspace"

    Use-BuildMutex {
        $baselineProject = Join-Path $script:Workspace 'Sample\Sample.csproj'
        $baseline = Invoke-Native { dotnet build $baselineProject -nologo -v:q -nodeReuse:false -p:UseSharedCompilation=false }
        if ($baseline.ExitCode -ne 0) { throw "The dirty fixtures do not compile before the cleanup:`n$($baseline.Output)" }
    }

    Use-BuildMutex { }
    Start-ExpVs
    Connect-Dte
    Write-Host 'Connected to the Experimental instance.'
    $script:MainWindow = [IntPtr](Get-Process -Id $script:VsPid).MainWindowHandle
    $script:DismissUnknown = $true
    $script:RegisteredNames = Resolve-Commands

    foreach ($name in $scenarios.Keys) {
        $selected = $false
        foreach ($pattern in $Scenario) { if ($name -like $pattern) { $selected = $true } }
        if (-not $selected) { continue }

        $script:CurrentScenario = $name
        if ($script:Aborted) { Check 'scenario ran' $false 'skipped: Visual Studio stopped responding earlier'; continue }
        Write-Host "--- $name"
        try {
            & $scenarios[$name]
            if ($script:Unexpected.Count -gt 0) {
                Check 'no unexpected dialog appeared' $false (($script:Unexpected | Select-Object -Unique) -join '; ')
            }
        }
        catch {
            Check 'scenario completed without error' $false ($_.Exception.Message + ' @ line ' + $_.InvocationInfo.ScriptLineNumber)
        }
        finally {
            Close-StrayWindows
        }
    }

    $failed = @($script:Results | Where-Object { -not $_.Ok })
    Write-Host ''
    Write-Host ("{0} check(s), {1} passed, {2} failed" -f $script:Results.Count, ($script:Results.Count - $failed.Count), $failed.Count)
    foreach ($item in $failed) { Write-Host ("FAIL  {0} :: {1}" -f $item.Scenario, $item.Check) -ForegroundColor Red }
    $exitCode = if ($failed.Count -eq 0 -and $script:Results.Count -gt 0) { 0 } else { 1 }
}
catch {
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
    $exitCode = 1
}
finally {
    Stop-ExpVs
    Remove-Workspace
}

exit $exitCode
