using System;
using System.IO;
using System.Reflection;

namespace ApiName
{
    // Cimatron discovers this class by interface at load time and calls
    // AppendCommand() to register the plugin's toolbar entry.
    public class ApiNamePlugin : CimUIInfrastructure.PlugIn.ICimApiCommandPlugin
    {
        public CimUIInfrastructure.PlugIn.ApiCommand AppendCommand()
        {
            var command = new CimUIInfrastructure.PlugIn.ApiCommand
            {
                Name = "ApiName",
                ToolbarName = "APIs",
                MenuPath = "__MENU_PATH__",
                Caption = "ApiName",
                ToolTip = "ApiName",
                Description = "ApiName plugin command.",
                Application =
                    CimUIInfrastructure.PlugIn.ApiApplications.Assembly |
                    CimUIInfrastructure.PlugIn.ApiApplications.Part,
                IconSource = new CimWpfContracts.WpfImageIdentifier(
                    Path.Combine(GetExecutionPath(), "icon.ico"),
                    CimWpfContracts.ImageSize.Small),
                ExecuteCommand = new ApiNameCommand()
            };

            return command;
        }

        public static string GetExecutionPath()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var location = assembly?.Location;
            return string.IsNullOrEmpty(location)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetDirectoryName(location);
        }
    }
}
