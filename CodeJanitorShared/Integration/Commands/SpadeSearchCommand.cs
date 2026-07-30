using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace CodeJanitor.Integration.Commands
{
    /// <summary>
    /// A command that provides for setting focus on the search bar in the Spade tool window.
    /// </summary>
    internal sealed class SpadeSearchCommand : BaseCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpadeSearchCommand" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        internal SpadeSearchCommand(CodeJanitorPackage package)
            : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeSearch)
        {
        }

        /// <summary>
        /// A singleton instance of this command.
        /// </summary>
        public static SpadeSearchCommand Instance { get; private set; }

        /// <summary>
        /// Initializes a singleton instance of this command.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>A task.</returns>
        public static async Task InitializeAsync(CodeJanitorPackage package)
        {
            Instance = new SpadeSearchCommand(package);
            await Instance.SwitchAsync(on: true);
        }

        /// <summary>
        /// Called to execute the command.
        /// </summary>
        protected override void OnExecute()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnExecute();

            var spade = Package.Spade;
            if (spade != null)
            {
                spade.SearchHost.Activate();
            }
        }
    }
}