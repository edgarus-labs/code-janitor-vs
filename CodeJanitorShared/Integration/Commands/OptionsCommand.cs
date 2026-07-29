using SteveCadwallader.CodeJanitor.Integration.Options;
using System.Threading.Tasks;

namespace SteveCadwallader.CodeJanitor.Integration.Commands
{
    /// <summary>
    /// A command that provides for launching the CodeJanitor Options to the general cleanup page.
    /// </summary>
    internal sealed class OptionsCommand : BaseCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsCommand" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        internal OptionsCommand(CodeJanitorPackage package)
            : base(package, PackageGuids.GuidCodeJanitorMenuSet, PackageIds.CmdIDCodeJanitorOptions)
        {
        }

        /// <summary>
        /// A singleton instance of this command.
        /// </summary>
        public static OptionsCommand Instance { get; private set; }

        /// <summary>
        /// Initializes a singleton instance of this command.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>A task.</returns>
        public static async Task InitializeAsync(CodeJanitorPackage package)
        {
            Instance = new OptionsCommand(package);
            await Instance.SwitchAsync(on: true);
        }

        /// <summary>
        /// Called to execute the command.
        /// </summary>
        protected override void OnExecute()
        {
            base.OnExecute();
            OptionsPageNavigation.SetPendingPage(null);
            Package.IDE.ExecuteCommand("Tools.Options");
        }
    }
}