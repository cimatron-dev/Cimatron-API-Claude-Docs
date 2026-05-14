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

            // Standard entry-point: grab the running Cimatron app and its
            // active document. Cast at the boundary — interop returns the
            // generic IUnknown-shaped type.
            interop.CimServicesAPI.CimApplicationProvider AppProvider = new interop.CimServicesAPI.CimApplicationProvider();
            var app = (interop.CimatronE.IApplication)AppProvider.GetApplication();
            var doc = (interop.CimBaseAPI.ICimDocument)app.GetActiveDoc();

            // TODO: implement the command behavior. `app` and `doc` are
            // your handles into Cimatron.
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
