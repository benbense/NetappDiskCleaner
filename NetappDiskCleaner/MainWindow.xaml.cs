using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NetappDiskCleaner
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string settingsFileIpKey = "IP";
        private const string settingsFileUsernameKey = "Username";
        private const string settingsFilePasswordKey = "Password";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            using (var client = new SshClient(IPAddress.Text, Username.Text, Password.Text))
            {
                client.Connect();

                Settings1.Default[settingsFileIpKey] = IPAddress.Text;
                Settings1.Default[settingsFileUsernameKey] = Username.Text;
                Settings1.Default[settingsFilePasswordKey] = Password.Text;
                Settings1.Default.Save();

                var command = client.CreateCommand("echo helo");
                var textResult = ExecuteAndPrintOutput(command);

                if (!textResult.Equals("helo\n"))
                {
                    client.Disconnect();
                    MessageBox.Show("There's an issue with the software, contact the developer");
                    return;
                }

                PrintToSSHCommTextBlock(textResult);

                var lsCommand = client.CreateCommand("ls");
                ExecuteAndPrintOutput(lsCommand);

                GetNodeName(client);
            }
        }

        private void GetNodeName(SshClient sshClient)
        {
            var result = CreateExecuteAndPrintCommand(sshClient, "node show");
        }

        private void PrintToSSHCommTextBlock(string output)
        {
            SSHCommTextBlock.Text = $"{SSHCommTextBlock.Text}{Environment.NewLine}{output}";
        }

        private string ExecuteAndPrintOutput(SshCommand sshCommand)
        {
            var output = sshCommand.Execute();
            PrintToSSHCommTextBlock(output);

            return output;
        }

        private string CreateExecuteAndPrintCommand(SshClient client, string commandText)
        {
            return ExecuteAndPrintOutput(client.CreateCommand(commandText));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IPAddress.Text = (string)Settings1.Default[settingsFileIpKey];
            Username.Text = (string)Settings1.Default[settingsFileUsernameKey];
            Password.Text = (string)Settings1.Default[settingsFilePasswordKey];
        }
    }
}
