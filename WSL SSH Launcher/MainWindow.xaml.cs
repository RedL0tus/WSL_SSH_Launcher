using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Serialization;

namespace WSL_SSH_Launcher
{
    public class Internal
    {
        // Make these public for XMLSerializer
        public bool startOnBoot = false;
        public string userName = "root";
        public string choosedDistro;
        [XmlIgnore]
        private DistrosInstalled distros;

        public Internal()
        {
            readDistros();
            choosedDistro = distros.defUID;
        }

        public struct Distro
        {
            public string UID;
            public string BasePath;
            public string DistributionName;

            public Distro(string id, RegistryKey dist)
            {
                UID = id;
                BasePath = dist.GetValue("BasePath").ToString();
                DistributionName = dist.GetValue("DistributionName").ToString();
            }
        }

        public struct DistrosInstalled
        {
            public string defUID;
            public Dictionary<string, Distro> distros;

            public DistrosInstalled(string def, Dictionary<string, Distro> distro)
            {
                defUID = def;
                distros = distro;
            }
        }

        public void readDistros()
        {
            // Modified from https://github.com/therealkenc/EnumWSL/blob/master/EnumWSL/Program.cs
            RegistryKey lxss = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Lxss");
            string defUID = lxss.GetValue("DefaultDistribution").ToString();
            string[] distroIDs = lxss.GetSubKeyNames();
            Dictionary<string, Distro> distroList = new Dictionary<string, Distro>();
            foreach (string distroID in distroIDs)
            {
                Distro currentDistro = new Distro(distroID, lxss.OpenSubKey(distroID));
                distroList[distroID] = currentDistro;
            }
            distros = new DistrosInstalled(defUID, distroList);
        }

        public bool getStartOnBoot()
        {
            return startOnBoot;
        }

        public bool setStartOnBoot(bool option)
        {
            startOnBoot = option;
            return getStartOnBoot();
        }

        public string getUserName()
        {
            return userName;
        }

        public string setUserName(string username)
        {
            userName = username;
            return getUserName();
        }

        public Distro[] getDistros()
        {
            return distros.distros.Values.ToArray<Distro>();
        }

        public Distro getDefaultDistro()
        {
            return distros.distros[distros.defUID];
        }

        public Distro getChoosedDistro()
        {
            Distro value = getDefaultDistro();
            if (!String.IsNullOrEmpty(choosedDistro))
            {
                distros.distros.TryGetValue(choosedDistro, out value);
            }
            return value;
        }

        public Distro setChoosedDistro(string name)
        {
            foreach (KeyValuePair<string, Distro> element in distros.distros)
            {
                if (element.Value.DistributionName == name)
                {
                    choosedDistro = element.Key;
                }
            }
            return getChoosedDistro();
        }

        public string runCommand(Action<string> func, string command)
        {
            Process process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.FileName = "wsl.exe";
            process.StartInfo.Arguments = "--distribution " + getChoosedDistro().DistributionName + " --user " + getUserName() + " " + command;
            string output = "";
            func("$ " + process.StartInfo.FileName + process.StartInfo.Arguments);
            DataReceivedEventHandler outputReceiver = new DataReceivedEventHandler((sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    string line = Regex.Replace(e.Data, "[^a-zA-Z0-9-_.:()/\\ ]+", "", RegexOptions.Compiled); // Avoid mysterious question marks
                    if (!String.IsNullOrEmpty(line))
                    {
                        output += line + "\n";
                        func(line);
                    }
                }
            });
            process.OutputDataReceived += outputReceiver;
            process.ErrorDataReceived += outputReceiver;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            process.Close();
            return output;
        }
    }

    public class InternalWrapper
    {
        public Internal inner;
        private XmlSerializer serializer = new XmlSerializer(typeof(Internal));
        private static string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SSH_Launcher");
        private static string fileName = "config.xml";
        private static string savePath = Path.Combine(basePath, fileName);
        private Action<string> dataReceivedEventHandler;
        public BackgroundWorker worker;

        public InternalWrapper()
        {
            if (File.Exists(savePath))
            {
                using (Stream reader = new FileStream(savePath, FileMode.Open))
                {
                    try
                    {
                        inner = (Internal)serializer.Deserialize(reader);
                    }
                    catch
                    {
                        inner = new Internal();
                    }
                }
                inner.readDistros();
            }
            else
            {
                inner = new Internal();
            }

            worker = new BackgroundWorker();
            worker.DoWork += worker_DoWork;
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
        }

        public void saveConfig()
        {
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }
            using (TextWriter writer = new StreamWriter(File.Open(savePath, FileMode.OpenOrCreate, FileAccess.ReadWrite)))
            {
                serializer.Serialize(writer, inner);
            }
        }

        public void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            string sshdLocation = inner.runCommand(dataReceivedEventHandler, "--exec sh -c \"which sshd\"");
            worker.ReportProgress(50); // SSHD going to start
            inner.runCommand(dataReceivedEventHandler, "--exec " + sshdLocation + " -dD");
            worker.ReportProgress(100); //SSHD exited
        }

        public void startSSHD(ProgressChangedEventHandler progressChangedEventHandler, Action<string> dataHandler)
        {
            dataReceivedEventHandler = dataHandler;
            worker.ProgressChanged += progressChangedEventHandler;
            worker.RunWorkerAsync();
        }

        public void stopSSHD()
        {
            inner.runCommand(dataReceivedEventHandler, "--terminate " + inner.getChoosedDistro().DistributionName);
        }

        public void setStartUp()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (key != null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                key.SetValue(assembly.GetName().Name, assembly.Location);
                inner.setStartOnBoot(true);
            }
            else
            {
                throw new InvalidOperationException("Registry key does not exist");
            }
        }

        public void unsetStartUp()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (key != null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                key.DeleteValue(assembly.GetName().Name);
                inner.setStartOnBoot(false);
            }
            else
            {
                throw new InvalidOperationException("Registry key does not exist");
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private InternalWrapper inner;
        private System.Windows.Forms.NotifyIcon icon;

        public MainWindow()
        {
            InitializeComponent();

            inner = new InternalWrapper();
            inner.saveConfig();


            // Initialize distribution selector and display available distributions
            addToLog("Available distributions:");
            foreach (Internal.Distro distro in inner.inner.getDistros())
            {
                addToLog(" - " + distro.DistributionName);
                distroComboBox.Items.Add(distro.DistributionName);
            }
            addToLog(inner.inner.getDistros().Length.ToString() + " distributions total");
            distroComboBox.Text = inner.inner.getChoosedDistro().DistributionName;

            // Initialize username textbox
            userNameTextBox.Text = inner.inner.getUserName();

            // Initialize tray icon
            icon = new System.Windows.Forms.NotifyIcon();
            icon.Icon = (Icon)CommonResource.startedIcon;
            icon.Visible = true;
            icon.DoubleClick += delegate (object sender, EventArgs args)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            };

            // Start on boot?
            checkBox.IsChecked = inner.inner.startOnBoot;
            if (inner.inner.startOnBoot)
            {
                inner.startSSHD(ProgressChangedHandler, addToLog);
            }

            // Set status to OK
            StatusStatusBar.Text = "OK";
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                this.Hide();
            base.OnStateChanged(e);
        }

        private void ProgressChangedHandler(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == 50)
            {
                changeInterfaceAfterStart();
            }
            else if (e.ProgressPercentage == 100)
            {
                changeInterfaceAfterStop();
            }
        }

        private void addToLog(string line)
        {
            Dispatcher.BeginInvoke(new ThreadStart(() => loggingTextBlock.Text += line + Environment.NewLine));
        }

        private void changeInterfaceAfterStart()
        {
            distroComboBox.IsEnabled = false;
            userNameTextBox.IsEnabled = false;
            startButton.Content = "Stop";
            StatusStatusBar.Text = "SSHD is running";
            icon.Icon = (Icon)CommonResource.startedIcon;
        }

        private void changeInterfaceAfterStop()
        {
            distroComboBox.IsEnabled = true;
            userNameTextBox.IsEnabled = true;
            startButton.Content = "Start";
            StatusStatusBar.Text = "Stopped";
            icon.Icon = (Icon)CommonResource.stoppedIcon;
        }

        private void DistroComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            string selected = cb.SelectedItem.ToString();
            inner.inner.setChoosedDistro(selected);
            Console.WriteLine("Distro selection changed to {0}", inner.inner.getChoosedDistro().DistributionName);
        }

        private void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(Path.Combine(inner.inner.getChoosedDistro().BasePath), "rootfs");
        }

        private void SshdConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // https://stackoverflow.com/a/4727164
            var args = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            args += ",OpenAs_RunDLL " + Path.Combine(inner.inner.getChoosedDistro().BasePath, "rootfs", "etc", "ssh", "sshd_config");
            Process.Start("rundll32.exe", args);
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (((string)startButton.Content == "Start") && (inner.worker.IsBusy != true))
            {
                inner.startSSHD(ProgressChangedHandler, addToLog);
            }
            else if (((string)startButton.Content == "Stop") && (inner.worker.WorkerSupportsCancellation == true))
            {
                MessageBoxResult result = MessageBox.Show(
                    "This application can only terminate all WSL instance of this distribution all at once, all working process will be killed, do you want to proceed?",
                    "Confirmation",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning
                    );
                if (result == MessageBoxResult.OK)
                {
                    inner.stopSSHD();
                }
            }
        }

        private void loggingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            loggingTextBlock.ScrollToEnd();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if ((string)startButton.Content == "Stop")
            {
                MessageBoxResult result = MessageBox.Show(
                    "WSL instance may survive after this launcher program exits, do you want to terminate it?\n\n" +
                    "Note: This application can only terminate all WSL instance of this distribution all at once, " +
                    "all working process will be killed.",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                    );
                if (result == MessageBoxResult.Yes)
                {
                    inner.stopSSHD();
                }
            }
            inner.saveConfig();
            icon.Visible = false;
        }

        private void UserNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            try
            {
                inner.inner.setUserName(tb.Text);
            }
            catch (NullReferenceException) { }
        }

        private void checkBox_Changed(object sender, RoutedEventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            try
            {
                if (cb.IsChecked.Value)
                {
                    inner.setStartUp();
                }
                else
                {
                    inner.unsetStartUp();
                }
            }
            catch
            {
                MessageBox.Show("Failed to set/unset autostart", "Could not comply", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
