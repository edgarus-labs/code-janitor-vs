# Cleanup Preview

## Availability

The first Preview Cleanup increment covers deterministic C# text transformations
in the selected-scope cleanup workflow. The implementation builds and has automated
regression coverage. An experimental-profile installation was verified on
2026-09-08 (installer success and matching assembly hashes); the interactive
Visual Studio runtime checklist below is still pending.

## Use the preview

1. Select files, folders or projects in Solution Explorer.
2. Run Code Janitor's **Cleanup Selected Code** command.
3. Choose configured settings or temporary settings for this operation.
4. Choose **Preview C# Text Changes** instead of **Start Cleanup**.
5. Select a file to inspect the read-only native Visual Studio diff. If the diff
   service is unavailable, the dialog displays **Before** and **After** text.
6. In **Rules**, clear any rule you do not want applied to that file. The result
   is recomputed from the original snapshot in the original pipeline order.
7. Clear **Apply** beside any file you want excluded from the operation.
8. Choose **Apply to Editors**, or **Cancel** to discard the plan.

## Safety and scope

- Planning reads open editor buffers, including unsaved changes. Closed files are
  read from disk without opening an editor or modifying the source file.
- Source text is compared again immediately before application. A changed file
  is skipped with a message; create a new preview rather than overwriting it.
- Only the approved output is applied. Applying does not rerun the full cleanup.
- Changes remain in editor buffers; the preview command does not save files.
  Normal user saves, autosave or other extensions may subsequently save them.
  If cleanup-on-save is enabled, a later save can run the ordinary cleanup.
- Application uses Code Janitor's existing undo transaction setting. It does not
  provide a disk-level rollback or an all-or-nothing transaction across files.
- Read-only or unavailable editors and per-file errors are reported. Successfully
  applied files are retained when another file fails; there is no silent rollback.
- No AI requests, Git staging, commits, generated files or temporary diff files
  are produced by this preview.
- Repository policy and the existing supported EditorConfig options determine
  which text rules are available. Unchecking a rule is temporary and file-specific.
- A rule marked **No change** did not alter this snapshot. It is not a guarantee
  that the rule applies semantically to the file.

## Current limitations

Only `.cs` files are supported in this increment. Other file types are listed as
skipped. Type splitting, AI documentation, namespace fixing, code reorganization,
third-party cleanup, Visual Studio formatting/removal of unused usings, and changes
to the file's disk encoding are not previewed or applied by this mode.

This is a preview of the existing deterministic text pipeline, not a compiler or
semantic-equivalence check. Review the result and run the relevant build/tests.
Preview preparation and rule recalculation currently run on the UI thread; use
bounded selections for large repositories. Cancellable background preparation is
still needed before treating this as a solution-scale preview.

## Visual Studio verification checklist

Status: **pending**. Build and unit tests do not verify MEF services, WPF bindings,
native diff rendering, editor undo behavior or the installed extension.

1. In an experimental instance, select a changed C# file, an unchanged C# file and
   a non-C# file. Verify the diff, statuses and disabled Apply cells as appropriate.
2. Toggle a rule off and on. Verify the diff updates and no earlier change survives
   after its rule is excluded. Exclude a file and verify it is not modified.
3. Cancel the dialog and verify editor text, dirty state and source bytes on disk
   are unchanged. Repeat with a closed file and an unsaved open document.
4. Apply and verify only selected buffers change, no files are saved, and Undo
   works with undo transactions enabled.
5. Change a closed file externally while the preview is open. Apply and verify
   the stale file is skipped with a message rather than overwritten.
6. Verify read-only files and unavailable editors are reported without losing
   successful changes to other files.
7. Check temporary settings are restored after both Cancel and Apply. Verify
   ordinary **Start Cleanup** still follows its original workflow.
8. Verify keyboard navigation, high DPI, smaller window sizes and light/dark
   themes. Open and close the preview repeatedly to check diff-viewer cleanup.

See the [delivery plan](todo/feature-delivery-plan.md) for subsequent increments.