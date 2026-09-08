using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using CodeJanitor.Helpers;

namespace CodeJanitor.NativeSettings
{
    [VisualStudioContribution]
    internal class ImportRulesCommand : Command
    {
        public ImportRulesCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("Code Janitor: Import Rules...");

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            var options = new FileDialogOptions
            {
                Title = "Import Code Janitor Rules",
                Filters = new DialogFilters(new[]
                {
                    new DialogFilter("Code Janitor Rules", ".config"),
                    new DialogFilter("All Files", ".*"),
                })
                {
                    DefaultFilterIndex = 0,
                },
            };

            var filePath = await this.Extensibility.Shell().ShowOpenFileDialogAsync(options, cancellationToken);
            if (filePath == null)
            {
                return;
            }

            CodeJanitorSettingsProvider.ImportSettingsFromFile(Properties.Settings.Default, filePath);

            // Push the freshly-imported values into the native settings store so any open
            // Settings UI reflects them immediately, without requiring a VS restart.
            await CodeJanitorNativeSettingsExtension.PushCurrentSettingsToNativeStoreAsync(this.Extensibility, cancellationToken);
        }
    }
}
