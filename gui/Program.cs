using System;
using System.Security.Principal;
using System.Windows.Forms;
using EgsLL.Forms;

namespace EgsLL
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Recovery requires folder rename/delete — best with admin rights.
            // Warn (don't block) if not elevated.
            if (!IsElevated())
            {
                var result = MessageBox.Show(
                    "EGS-LL works best when run as Administrator.\n\n" +
                    "Without elevation, folder operations may fail if the game\n" +
                    "is installed in a protected location (e.g. Program Files).\n\n" +
                    "Continue anyway?",
                    "EGS-LL -- Elevation Notice",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (result != DialogResult.Yes)
                    return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
