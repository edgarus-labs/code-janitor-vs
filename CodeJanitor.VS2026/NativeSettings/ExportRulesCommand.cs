using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using CodeJanitor.Helpers;

namespace CodeJanitor.NativeSettings
{
    // Companion to the native Settings migration: the native Settings API has no concept of
    // per-solution scope, so this replaces the old silent CodeJanitor.config auto-load with an
    // explicit, user-initiated way to share cleanup rules between machines/teams.
    [VisualStudioContribution]
    internal class ExportRulesCommand : Command
    {
        public ExportRulesCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("Code Janitor: Export Rules...");

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            var options = new FileDialogOptions
            {
                Title = "Export Code Janitor Rules",
                InitialFileName = "CodeJanitor.config",
                Filters = new DialogFilters(new[]
                {
                    new DialogFilter("Code Janitor Rules", ".config"),
                    new DialogFilter("All Files", ".*"),
                })
                {
                    DefaultFilterIndex = 0,
                },
            };

            var filePath = await this.Extensibility.Shell().ShowSaveAsFileDialogAsync(options, cancellationToken);
            if (filePath == null)
            {
                return;
            }

            CodeJanitorSettingsProvider.ExportSettingsToFile(Properties.Settings.Default, filePath);
        }
    }
}
