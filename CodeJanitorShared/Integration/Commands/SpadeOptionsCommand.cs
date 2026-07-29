using SteveCadwallader.CodeJanitor.Integration.Options;
using SteveCadwallader.CodeJanitor.UI.Dialogs.Options.Digging;
using System.Threading.Tasks;

namespace SteveCadwallader.CodeJanitor.Integration.Commands
{
    /// <summary>
    /// A command that provides for launching the CodeJanitor Options to the Spade page.
    /// </summary>
    internal sealed class SpadeOptionsCommand : BaseCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpadeOptionsCommand" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        internal SpadeOptionsCommand(CodeJanitorPackage package)
            : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorSpadeOptions)
        {
        }

        /// <summary>
        /// A singleton instance of this command.
        /// </summary>
        public static SpadeOptionsCommand Instance { get; private set; }

        /// <summary>
        /// Initializes a singleton instance of this command.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>A task.</returns>
        public static async Task InitializeAsync(CodeJanitorPackage package)
        {
            Instance = new SpadeOptionsCommand(package);
            await Instance.SwitchAsync(on: true);
        }

        /// <summary>
        /// Called to execute the command.
        /// </summary>
        protected override void OnExecute()
        {
            base.OnExecute();
            OptionsPageNavigation.SetPendingPage(typeof(DiggingViewModel));
            Package.IDE.ExecuteCommand("Tools.Options");
        }
    }
}