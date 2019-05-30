using System.Threading;
using System.Windows;

namespace WSL_SSH_Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "WSL_SSH_Launcher";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Only one instance is allowed at a time");
                Current.Shutdown();
            }

            base.OnStartup(e);
        }
    }
}
