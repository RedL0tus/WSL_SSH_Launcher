using System;
using System.Threading;
using System.Windows;

namespace WSL_SSH_Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string UniqueEventName = "8ecd7d80-37b0-4a77-a6c8-b1efc1bf9f06";
        private const string UniqueMutexName = "df17a2ac-d53d-4b65-86b5-f790efa05315";
        private EventWaitHandle eventWaitHandle;
        private Mutex mutex;

        // https://stackoverflow.com/a/23730146
        private void AppOnStartup(object sender, StartupEventArgs e)
        {
            bool isOwned;
            mutex = new Mutex(true, UniqueMutexName, out isOwned);
            eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, UniqueEventName);

            GC.KeepAlive(mutex);

            if (isOwned)
            {
                var thread = new Thread(
                    () =>
                    {
                        while (eventWaitHandle.WaitOne())
                        {
                            Current.Dispatcher.BeginInvoke((Action)(() => ((MainWindow)Current.MainWindow).BringToForeground()));
                        }
                    });
                thread.IsBackground = true;
                thread.Start();
                return;
            }

            eventWaitHandle.Set();

            Shutdown();
        }
    }
}
