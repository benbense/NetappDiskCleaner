using NetappDiskCleaner.Model;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
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
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private const string settingsFileIpKey = "IP";
        private const string settingsFileUsernameKey = "Username";
        private const string settingsFilePasswordKey = "Password";

        private SshClient sshClient;
        private ShellStream stream;

        private List<Disk> _allDisks;

        public List<Node> nodes;
        public List<Aggregate> aggregates;

        public List<Disk> AllDisks
        {
            get
            {
                return _allDisks;
            }
            set
            {
                _allDisks = value;
                NotifyPropertyChanged();
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<Disk> brokenDisks;
        public List<Disk> foreignDisks;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsState(false);

            sshClient = new SshClient(IPAddress.Text, Username.Text, Password.Password);

            try
            {
                sshClient.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occured while connecting : \n" + ex.Message);
                SetButtonsState(true);
                sshClient.Dispose();
            }

            Settings1.Default[settingsFileIpKey] = IPAddress.Text;
            Settings1.Default[settingsFileUsernameKey] = Username.Text;
            Settings1.Default[settingsFilePasswordKey] = Password.Password;
            Settings1.Default.Save();

            var modes = new Dictionary<Renci.SshNet.Common.TerminalModes, uint>();

            stream = sshClient.CreateShellStream("xterm", 255, 50, 800, 600, 1024, modes);

            WakeupCall(stream);

            ExecuteNetappEnvironmentCommands(stream);

            GetONTAPVersion(stream);

            nodes = GetNodes(stream);

            if (nodes?.Count == 0)
            {
                MessageBox.Show("No nodes were found! Make sure NetApp is configured well or contact the developer");
                return;
            }

            aggregates = GetAggregates(stream);
            AllDisks = GetDisks(stream);
            ShowAllDisks(AllDisks);

            // TODO - Show user the broken disks
            brokenDisks = GetBrokenDisks(AllDisks);
            ShowAllBrokenDisks(brokenDisks);

            foreignDisks = GetForeignDisksFromDisks(AllDisks, nodes);
            ShowAllForeignDisks(foreignDisks);
        }

        private void GetONTAPVersion(ShellStream client)
        {
            var result = ExecuteAndPrintOutput(client, "version");

            var lines = result.Split(":");
            var netappVersion = lines[0];
            if (!netappVersion.Contains("NetApp Release"))
            {
                ONTAPVersion.Foreground = System.Windows.Media.Brushes.Red;
                ONTAPVersion.Content = "Error retrieving ONTAP version!";
            }
            else
            {
                ONTAPVersion.Foreground = System.Windows.Media.Brushes.Green;
                ONTAPVersion.Content = lines[0];
            }
        }

        private void SetButtonsState(bool isEnabled)
        {
            IPAddress.IsEnabled = isEnabled;
            Username.IsEnabled = isEnabled;
            Password.IsEnabled = isEnabled;
            ConnectButton.IsEnabled = isEnabled;
            ExecuteButton.IsEnabled = !isEnabled;
        }

        private void ShowAllDisks(List<Disk> disks)
        {
            AllDisksCount.Content = disks.Count.ToString();
        }

        private void ShowAllForeignDisks(List<Disk> disks)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Found {disks.Count} foreign disks");
            foreach (var disk in disks)
            {
                sb.AppendLine(disk.ClusterName);
            }
            MessageBox.Show(sb.ToString());

            ForeignDisksCount.Content = disks.Count.ToString();
            if (disks.Count <= 0)
            {
                ForeignDisksCount.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                ForeignDisksCount.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private void ShowAllBrokenDisks(List<Disk> disks)
        {
            if (disks.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {disks.Count} broken disks");
                foreach (var disk in disks)
                {
                    sb.AppendLine(disk.ClusterName);
                }
                MessageBox.Show(sb.ToString());

                ForeignDisksCount.Content = disks.Count.ToString();
                ForeignDisksCount.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private List<Disk> GetBrokenDisks(List<Disk> disks)
        {
            return disks.Where(disk => disk.ContainerType == ContainerType.Broken).ToList();
        }

        private void ZerospareDisksIfNeeded(ShellStream stream, List<Disk> disks)
        {
            var newLine = "\r\n";

            if (disks.All(disk => disk.Type == DiskType.SSD))
            {
                ExecuteAndPrintOutput(stream, "disk zerospares");

                bool partyPooper = true;

                while (partyPooper)
                {
                    Thread.Sleep(1000);

                    partyPooper = false;

                    var zeroedCmdOutput = ExecuteAndPrintOutput(stream, "disk show -fields zeroed");

                    var dirtyZeroedDisksStatus = zeroedCmdOutput.Substring(zeroedCmdOutput.FindXOccurenceOfSubstring(newLine, 2) + newLine.Length);

                    var zeroedDisksStatusNewLineIndexes = dirtyZeroedDisksStatus.GetIndexesOfOccurencesOfSubstring(newLine);

                    for (int i = 0; i < zeroedDisksStatusNewLineIndexes.Count - 2; i++)
                    {
                        var currentNewLineIndex = zeroedDisksStatusNewLineIndexes[i];
                        var nextNewLineIndex = zeroedDisksStatusNewLineIndexes[i + 1];
                        var row = dirtyZeroedDisksStatus.Substring(zeroedDisksStatusNewLineIndexes[i] + newLine.Length, nextNewLineIndex - currentNewLineIndex - newLine.Length);
                        var status = Convert.ToBoolean(row.Split(";")[1]);

                        if (!status)
                        {
                            partyPooper = true;
                            break;
                        }
                    }
                }
            }
        }

        private void ExitNodeShell(ShellStream stream)
        {
            ExecuteAndPrintOutput(stream, "exit");
        }

        private void UnpartitionDisks(ShellStream stream, List<Disk> disks)
        {
            disks.ForEach(foreignDisk =>
            {
                ExecuteAndPrintOutput(stream, $"disk unpartition {foreignDisk.NodeName}");

                // TODO - See if we need a double confirmation for some reason
                ExecuteAndPrintOutput(stream, "y");
            });
        }

        private void EnrichDisksViaNodeShell(ShellStream stream, List<Disk> disks)
        {
            var diskShowOutput = ExecuteAndPrintOutput(stream, $"disk show -m");
            var outputAfterTwoRows = diskShowOutput.Substring(diskShowOutput.FindXOccurenceOfSubstring(Environment.NewLine, 2) + Environment.NewLine.Length);

            var allNewLineIndexes = outputAfterTwoRows.GetIndexesOfOccurencesOfSubstring("\n");

            outputAfterTwoRows = outputAfterTwoRows.Substring(0, allNewLineIndexes[allNewLineIndexes.Count - 1]);


            foreach (var row in outputAfterTwoRows.Split("\n"))
            {
                var partitions = new List<int>();
                var isPartition = false;
                int partitionNumber = -1;

                if (row.Contains("PRkey="))
                {
                    continue;
                }

                var rowParts = row.Split(" ");

                var diskNodeName = rowParts[0];

                if (diskNodeName.Contains("P"))
                {
                    isPartition = true;
                    var diskNodeAndPartition = diskNodeName.Split("P");
                    diskNodeName = diskNodeAndPartition[0];
                    partitionNumber = int.Parse(diskNodeAndPartition[1]);
                }

                int wordsSkipped = 0;

                string clusterName = null;

                for (int i = 1; i < rowParts.Length; i++)
                {
                    if (rowParts[i] != string.Empty)
                    {
                        wordsSkipped++;
                    }

                    if (wordsSkipped == 5)
                    {
                        var partWithClusterName = rowParts[i];

                        var wordsInPart = partWithClusterName.Split("\t");

                        if (isPartition)
                        {
                            clusterName = wordsInPart[2].Split(".P")[0];
                        }
                        else
                        {
                            clusterName = wordsInPart[2];
                        }

                        break;
                    }
                }

                if (clusterName == null)
                {
                    MessageBox.Show("Couldnt find cluster name of a disk in node mode! (disk show -m)");
                    continue;
                }

                var matchingDisk = disks.FirstOrDefault(foreignDisk => foreignDisk.ClusterName == clusterName);

                if (matchingDisk == null)
                {
                    continue;
                }

                if (isPartition)
                {
                    if (matchingDisk.Partitions == null)
                    {
                        matchingDisk.Partitions = new List<int>();
                    }

                    matchingDisk.Partitions.Add(partitionNumber);
                }
                else
                {
                    matchingDisk.NodeName = diskNodeName;
                }
            }
        }

        private void SetEnvironmentAndEnterNodeShell(ShellStream stream, Node firstOwnedNode)
        {
            ExecuteAndPrintOutput(stream, $"node run -node {firstOwnedNode.Name}");
            ExecuteAndPrintOutput(stream, "priv set advanced");
        }

        private void RemoveStaleRecordFromAggregates(ShellStream stream, List<Aggregate> foreignAggregates)
        {
            foreignAggregates.ForEach(foreignAggregate =>
            {
                ExecuteAndPrintOutput(stream, $"aggr remove-stale-record -aggregate {foreignAggregate.Name}");
            });
        }

        private IEnumerable<Aggregate> GetForeignAggregates(ShellStream stream, List<Aggregate> ownedAggregates)
        {
            var detailedAggregatesStatus = ExecuteAndPrintOutput(stream, "aggr status -r", "entries were displayed.");

            var aggregateSepeartor = "Aggregate: ";

            var aggregateIndexes = detailedAggregatesStatus.GetIndexesOfOccurencesOfSubstring(aggregateSepeartor);

            for (int i = 0; i < aggregateIndexes.Count; i++)
            {
                var index = aggregateIndexes[i];

                var stringFromAggregateTitle = detailedAggregatesStatus.Substring(index + aggregateSepeartor.Length);

                var indexOfSpaceAfterAggrName = stringFromAggregateTitle.IndexOf(" ");

                var aggregateName = stringFromAggregateTitle.Substring(0, indexOfSpaceAfterAggrName);

                if (!ownedAggregates.Exists(ownedAggr => ownedAggr.Name == aggregateName))
                {
                    yield return new Aggregate() { Name = aggregateName };
                }
            }
        }

        private void AssignDisksToNode(ShellStream stream, List<Disk> disks, Node node)
        {
            disks.ForEach(disk =>
            ExecuteAndPrintOutput(
                stream,
                $"disk assign -disk {disk.ClusterName} -owner {node.Name}"));
        }

        private void RemoveOwnerOfDisksNodeMode(ShellStream client, List<Disk> disks)
        {
            disks.ForEach(disk =>
            {
                if (disk.Partitions != null)
                {
                    foreach (var partition in disk.Partitions.OrderByDescending(x => x))
                    {
                        ExecuteAndPrintOutput(client, $"disk remove_ownership {disk.NodeName}P{partition}");
                        ExecuteAndPrintOutput(client, $"y");
                    }
                }
                ExecuteAndPrintOutput(client, $"disk remove_ownership {disk.NodeName}");
                ExecuteAndPrintOutput(client, $"y");
            });
        }

        private List<Disk> GetForeignDisksFromDisks(List<Disk> allDisks, List<Node> nodes)
        {
            return allDisks.Where(disk => !nodes.Exists(node => node.Name == disk.OwnerName) && disk.ContainerType != ContainerType.Broken).ToList();
        }

        private List<Disk> GetDisks(ShellStream stream)
        {
            var result = ExecuteAndPrintOutput(stream, "disk show -fields disk,type,container-type,owner");

            var disksIndex = result.Substring(result.FindXOccurenceOfSubstring("\n", 2)).Trim();

            var lines = disksIndex.Split("\n");

            var disks = new List<Disk>();

            for (int i = 0; i < lines.Length - 2; i++)
            {
                var aggrFields = lines[i].Split(";");
                disks.Add(new Disk()
                {
                    ClusterName = aggrFields[0],
                    OwnerName = aggrFields[1],
                    ContainerType = StringToContainerType(aggrFields[2]),
                    Type = StringToDiskType(aggrFields[3]),
                    NodeName = null
                });
            }

            return disks;
        }

        private DiskType StringToDiskType(string v)
        {
            return v.ToLower() switch
            {
                "sas" => DiskType.SAS,
                "ssd" => DiskType.SSD,
                _ => DiskType.DontCare
            };
        }

        private ContainerType StringToContainerType(string v)
        {
            return v.ToLower() switch
            {
                "spare" => ContainerType.Spare,
                "unassigned" => ContainerType.Unassigned,
                "shared" => ContainerType.Shared,
                "broken" => ContainerType.Broken,
                _ => ContainerType.DontCare,
            };
        }

        private List<Aggregate> GetAggregates(ShellStream stream)
        {
            var result = ExecuteAndPrintOutput(stream, "aggr show -fields aggregate,owner-name,state");

            var aggregatesIndex = result.Substring(result.FindXOccurenceOfSubstring("\n", 2)).Trim();

            var lines = aggregatesIndex.Split("\n");

            var aggregates = new List<Aggregate>();

            for (int i = 0; i < lines.Length - 2; i++)
            {
                var aggrFields = lines[i].Split(";");
                aggregates.Add(new Aggregate()
                {
                    Name = aggrFields[0],
                    OwnerNode = aggrFields[1],
                    IsOnline = aggrFields[2] == "online"
                });
            }

            return aggregates;
        }

        private void WakeupCall(ShellStream stream)
        {
            ExecuteAndPrintOutput(stream, "");
        }

        public string SendCommand(string customCMD, ShellStream mixer)
        {
            string answer;

            var reader = new StreamReader(mixer);
            var writer = new StreamWriter(mixer);
            writer.AutoFlush = true;
            WriteStream(customCMD, writer, mixer);
            answer = ReadStream(reader);
            return answer;
        }

        private void WriteStream(string cmd, StreamWriter writer, ShellStream stream)
        {
            writer.WriteLine(cmd);
            while (stream.Length == 0)
            {
                Thread.Sleep(500);
            }
        }

        private string ReadStream(StreamReader reader)
        {
            StringBuilder result = new StringBuilder();

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                result.AppendLine(line);
            }

            return result.ToString();
        }

        private void ExecuteNetappEnvironmentCommands(ShellStream client)
        {
            ExecuteAndPrintOutput(client, "set -confirmations off");
            ExecuteAndPrintOutput(client, "set advanced");
            ExecuteAndPrintOutput(client, "set -showseparator \";\"");
            ExecuteAndPrintOutput(client, "row 0");
        }

        private List<Node> GetNodes(ShellStream client)
        {
            var result = ExecuteAndPrintOutput(client, "node show -fields node");

            var nodesIndex = result.Substring(result.FindXOccurenceOfSubstring("\n", 2)).Trim();

            var lines = nodesIndex.Split("\n");

            var nodes = new List<Node>();

            for (int i = 0; i < lines.Length - 2; i++)
            {
                nodes.Add(new Node() { Name = lines[i].Split(";")[0] });
            }

            return nodes;
        }

        private void PrintToSSHCommTextBlock(string output)
        {
            SSHCommTextBlock.Text = $"{SSHCommTextBlock.Text}{Environment.NewLine}{output}";
        }

        private string ExecuteAndPrintOutput(ShellStream client, string commandText, string textToExpect = ">")
        {
            client.Write(commandText + "\n");

            string output;

            if (textToExpect != null)
            {
                client.Expect(commandText);
                output = client.Expect(textToExpect); //this need to expect also confirmation prompts in node mode like "are you ready to continue?"
            }
            else
            {
                while (!client.DataAvailable)
                {
                    Thread.Sleep(500);
                }

                output = client.Read();
            }

            client.Flush();

            var fixedOutput = output.Trim();

            PrintToSSHCommTextBlock(fixedOutput);

            return fixedOutput;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IPAddress.Text = (string)Settings1.Default[settingsFileIpKey];
            Username.Text = (string)Settings1.Default[settingsFileUsernameKey];
            Password.Password = (string)Settings1.Default[settingsFilePasswordKey];
        }

        private void ShowAllDisks_Click(object sender, RoutedEventArgs e)
        {
        }

        private void ShowAllForeignDisks_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult executeConfirmationMessageBox = MessageBox.Show("This will start the cleanup process, are you sure you want to continue?", "Cleanup Confirmation", MessageBoxButton.YesNo);
            if (executeConfirmationMessageBox == MessageBoxResult.Yes)
            {


                var firstOwnedNode = nodes[0];

                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                EnrichDisksViaNodeShell(stream, foreignDisks);

                RemoveOwnerOfDisksNodeMode(stream, foreignDisks);

                ExitNodeShell(stream);

                AssignDisksToNode(stream, foreignDisks, firstOwnedNode);

                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                AssignDisksToNodeNodeMode(stream, foreignDisks, firstOwnedNode);

                ExitNodeShell(stream);
                var foreignAggregates = GetForeignAggregates(stream, aggregates).ToList();
                RemoveStaleRecordFromAggregates(stream, foreignAggregates);

                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                UnpartitionDisks(stream, foreignDisks);

                ExitNodeShell(stream);

                ZerospareDisksIfNeeded(stream, foreignDisks);

                RemoveOwnerOfDisksClusterMode(stream, foreignDisks);
            }
        }

        private void RemoveOwnerOfDisksClusterMode(ShellStream stream, List<Disk> disks)
        {
            foreach (var disk in disks)
            {
                ExecuteAndPrintOutput(stream, $"disk removeowner {disk.ClusterName}");
            }
        }

        private void AssignDisksToNodeNodeMode(ShellStream stream, List<Disk> disks, Node node)
        {
            foreach (var disk in disks)
            {
                foreach (var partition in disk.Partitions.OrderByDescending(x => x))
                {
                    ExecuteAndPrintOutput(stream, $"disk assign {disk.NodeName}P{partition}");
                }
            }
        }
    }
}
