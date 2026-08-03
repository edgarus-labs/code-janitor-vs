# Status: Reorganizing member-type grid implemented, but extension not appearing in Exp VS after deploy

Date: 2026-07-30

## What was done this session

1. **UX fix for the 12 `Reorganizing_MemberTypeX` native settings.** These previously rendered
   in the native Settings UI as raw serialized strings (e.g. `"Destructors||3||Destructors"`).
   Replaced with a proper grid, using the same `Setting.ObjectArray` primitive that backs
   grid-style UIs like the NuGet Package Manager sources list:
   - `CodeJanitor.VS2026/NativeSettings/ReorganizingNativeSettings.cs` - single
     `Setting.ObjectArray MemberTypes` (3 columns: Type [readonly], Order [int], Display name
     [string]; 12 fixed rows, `AllowAdditionsAndRemovals = false`). Rows had to be written out
     as literal inline `new ArraySettingItem { ... }` entries - the `[VisualStudioContribution]`
     source generator statically parses these initializers and rejects any local helper
     *method* call with `CEE0018 (compile-time constant evaluation failed)`.
   - New `CodeJanitor.VS2026/NativeSettings/MemberTypeArrayItem.cs` - implements
     `IArraySettingItemConvertible`, bridges grid rows to/from the existing
     `CodeJanitor.Helpers.MemberTypeSetting` serialized-string type. Registered in
     `CodeJanitor.VS2026.csproj`'s explicit `<Compile Include>` list
     (`EnableDefaultItems=false`).
   - `CodeJanitor.VS2026/NativeSettings/CodeJanitorNativeSettingsExtension.cs` - updated the 3
     bridge points (settings array, push-to-native-store batch, change-handler) to use the
     single `MemberTypes` setting instead of the old 12 raw-string settings.
   - Verified: `CodeJanitor.VS2026.csproj` builds with `/p:DeployExtension=false`, 0 errors.

2. **Fixed a real bug in `scripts/deploy-exp.ps1`.** `devenv /rootsuffix Exp /updateconfiguration`
   hands the actual pkgdef merge off to a background `devenv.exe` worker process (visible with
   just `/updateConfiguration` on its command line, no `/rootsuffix`) and returns almost
   immediately itself - it does **not** block until the merge is actually done. The script was
   checking `privateregistry.bin` right after, while the worker still held it open, causing an
   unhandled `IOException` that killed the whole deploy.
   - Added `Wait-UpdateConfigurationWorker` (polls for that worker process to exit, up to 180s)
     called after each `/updateconfiguration` invocation.
   - Wrapped the `Test-PkgDefMerged` file-open in try/catch so a still-locked file is treated as
     "not verified yet" (triggering the existing retry path) instead of throwing and aborting.
   - Re-ran deploy end-to-end: build succeeded (0 errors/0 warnings), VSIX install accepted,
     `/updateconfiguration` retried once automatically, **"pkgdef merge verified in
     privateregistry.bin."** printed, devenv Exp launched at the end.

## Current unresolved problem

Despite the deploy script now completing successfully end-to-end (build OK, install OK, pkgdef
merge verified present in `privateregistry.bin`, devenv relaunched), **the user reports "Code
Janitor" still does not appear** in the native Settings UI / Options in the launched
`devenv /rootsuffix Exp` instance. This has been checked twice after two separate clean
redeploys (`-CleanHive -LaunchVS`) and is still not showing up.

This is a genuine open issue - the previously-known failure mode (pkgdef merge silently not
completing) has been fixed and verified, yet the symptom persists, so there is likely a
**second, different root cause** still to be found.

## Not yet investigated (next steps for next session)

- Check `ActivityLog.xml` in the Exp hive
  (`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_ec255184Exp\ActivityLog.xml`) for load errors /
  exceptions related to CodeJanitor on this specific launch (ideally regenerate it fresh: delete
  then relaunch devenv with `/Log`).
- Confirm which hive devenv actually launched into this time - `deploy-exp.ps1` and the manual
  `Start-Process devenv /rootsuffix Exp` calls in the terminal history should target the same
  `ec255184Exp` hive, but worth double-checking there isn't a second/default hive confusion.
- Re-verify (as done earlier this session for the prior grid work) that the built VSIX's
  `.vsextension/settingsRegistration.json` still contains the expected categories/properties -
  confirm this build didn't regress that.
- Check for duplicate/stale `Extensions\<hash>\` folders again (`Get-ChildItem
  $expHive\Extensions -Directory`) - this has caused "looks uninstalled" symptoms before even
  when pkgdef merge succeeded, if an older duplicate copy wins.
- Consider whether a full hive wipe (not just `-CleanHive`'s selective cache clear) is needed
  again, per the `STATUS UPDATE 7` precedent in
  `/memories/session/native-settings-migration-plan.md` (duplicate/stale UI state has previously
  required a full folder wipe of `18.0_ec255184Exp`, not just clearing sub-caches).
- Confirm the extension is even being loaded at all (not just its settings) - e.g. check if any
  other CodeJanitor UI (tool window, commands via Ctrl+Q) is present, to narrow down whether this
  is a Settings-registration-specific problem or the whole extension failing to load.

**Per explicit user instruction: no further code changes were made after writing this file -
this is a pause/checkpoint for the next session to pick up investigation.**
