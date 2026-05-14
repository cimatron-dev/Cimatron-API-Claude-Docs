using ApiName.Helpers;

namespace ApiName
{
    // Called by Cimatron when the user clicks the plugin command. Add the
    // actual feature logic in OnCommand (single click) or OnCommandDblClk.
    internal class ApiNameCommand : CimUIInfrastructure.Commands.ICimWpfCommand
    {
        public bool OnCommand()
        {
            Logger.LogInfo("ApiName command invoked.");
            // TODO: implement the command behavior.
            return true;
        }

        public bool OnCommandDblClk()
        {
            return OnCommand();
        }

        public CimUIInfrastructure.Commands.CimWpfUICommandStates OnCommandUI()
        {
            return new CimUIInfrastructure.Commands.CimWpfUICommandStates
            {
                UiState = CimUIInfrastructure.Commands.CommandUIState.Enabled
            };
        }

        public string GetAccelerator() => string.Empty;

        public void SetAccelerator(string accelerator) { }
    }
}
