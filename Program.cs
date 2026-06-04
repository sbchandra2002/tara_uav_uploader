using System;
using System.Windows.Forms;

namespace TARAFlasher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var splash = new SplashForm())
                splash.ShowDialog();

            var mainForm = new AdvancedMainForm();
            mainForm.Shown += (s, e) => { mainForm.BringToFront(); mainForm.Activate(); };
            Application.Run(mainForm);
        }
    }
}
