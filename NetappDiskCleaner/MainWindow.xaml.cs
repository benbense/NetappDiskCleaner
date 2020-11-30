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
using System.Windows.Threading;

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
        private bool areStreamsOpen = false;

        private List<Disk> _allDisks;
        private List<Disk> _displayedDisks;
        private List<Disk> _foreignDisks;
        private List<Disk> _brokenDisks;
        private List<Disk> _unsupportedDisks;

        public List<Node> nodes;
        public List<Aggregate> aggregates;

        private bool isSingleNode;

        public string NetappVersion;

        public List<Disk> DisplayedDisks
        {
            get
            {
                return _displayedDisks;
            }
            set
            {
                _displayedDisks = value;
                NotifyPropertyChanged();
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {

            CleanUI();

            SetButtonsState(false);

            sshClient = new SshClient(IPAddress.Text, Username.Text, Password.Password);

            try
            {
                sshClient.Connect();
            }
            catch (Exception ex)
            {
                UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Error);
                MessageBox.Show("An error occured while connecting : \n" + ex.Message, "Error!");
                SetButtonsState(true);
                sshClient.Dispose();
            }

            areStreamsOpen = true;

            Title = Title + $" - {IPAddress.Text}";

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

            CheckSingeNodeCluster();

            if (nodes?.Count == 0)
            {
                UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Error);
                MessageBox.Show("No nodes were found! Make sure NetApp is configured well or contact the developer", "Error!");
                return;
            }

            aggregates = GetAggregates(stream);
            _allDisks = GetDisks(stream);
            ShowAllDisks(_allDisks);

            _brokenDisks = GetBrokenDisks(_allDisks);
            ShowAllBrokenDisks(_brokenDisks);

            _unsupportedDisks = GetUnsupportedDisks(_allDisks);
            ShowAllUnsupportedDisks(_unsupportedDisks);

            if (!isSingleNode && _unsupportedDisks.Count > 0)
            {
                UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Paused);
                MessageBox.Show($"Disovered {_unsupportedDisks.Count} unsupported disks, this might happen when using IOM's with different versions on a system with more that 1 node. {Environment.NewLine}You have {nodes.Count} nodes in your system, {nodes.Where(node => node.Health).Count()} of which are healthy. {Environment.NewLine}Check your IOM's versions.");
            }

            _foreignDisks = GetForeignDisksFromDisks(_allDisks, nodes, _unsupportedDisks);
            ShowAllForeignDisks(_foreignDisks);

            DisplayedDisks = _allDisks;

            Checkboxes.IsEnabled = true;
        }

        private void CheckSingeNodeCluster()
        {
            isSingleNode = nodes.Where(node => node.Health).Count() == 1;
        }

        private void UpdateTaskbarErrorState(double progressValue, System.Windows.Shell.TaskbarItemProgressState progressState)
        {
            taskbarInfo.ProgressValue = progressValue;
            taskbarInfo.ProgressState = progressState;
        }

        private void UpdateTaskbarErrorState(double progressValue, System.Windows.Shell.TaskbarItemProgressState progressState, string description)
        {
            UpdateTaskbarErrorState(progressValue, progressState);

            taskbarInfo.Description = description;
        }

        private void ShowAllUnsupportedDisks(List<Disk> disks)
        {
            if (disks.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {disks.Count} unsupported disks");
                foreach (var disk in disks)
                {
                    sb.AppendLine(disk.ClusterName);
                }
                MessageBox.Show(sb.ToString(), $"{disks.Count} Unsupported Disks");

                UnsupportedDisksCount.Content = disks.Count.ToString();
                UnsupportedDisksCount.Foreground = System.Windows.Media.Brushes.Red;
            }
            UnsupportedDisksCount.Content = disks.Count.ToString();
            if (disks.Count <= 0)
            {
                UnsupportedDisksCount.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                UnsupportedDisksCount.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private List<Disk> GetUnsupportedDisks(List<Disk> disks)
        {
            var emptyDiskList = new List<Disk>();
            if (!isSingleNode)
            {
                return disks.Where(disk => disk.ClusterName.Contains(":") && disk.ContainerType != ContainerType.Broken).ToList();
            }
            else
            {
                return emptyDiskList;
            }
        }
        private void GetONTAPVersion(ShellStream client)
        {
            var result = ExecuteAndPrintOutput(client, "version");

            var lines = result.Split(":");
            var netappVersion = lines[0];
            var versionExtract = lines[0].Split(" ");
            var netappVersionExtracted = versionExtract[2].Split("P");
            double ontapMainVersion = Convert.ToDouble(netappVersionExtracted[0]);
            int ontapPReleaseVersion = Convert.ToInt32(netappVersionExtracted[1]);

            if (!netappVersion.Contains("NetApp Release"))
            {
                ONTAPVersion.Foreground = System.Windows.Media.Brushes.Red;
                ONTAPVersion.Content = "Invalid ONTAP version!";
            }
            else
            {
                if (ontapMainVersion == 9.7)
                {
                    ONTAPVersion.Foreground = System.Windows.Media.Brushes.Green;
                    ONTAPVersion.Content = lines[0];
                }
                else
                {
                    ONTAPVersion.Foreground = System.Windows.Media.Brushes.Orange;
                    ONTAPVersion.Content = lines[0];
                    UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Paused);
                    MessageBox.Show($"Version {ontapMainVersion}P{ontapPReleaseVersion} is not tested yet and might not be supported", "Check ONTAP Version");
                    ONTAPVersion.ToolTip = $"Version {ontapMainVersion}P{ontapPReleaseVersion} might not be supported";
                }
            }

            NetappVersion = netappVersion;
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
            if (disks.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {disks.Count} foreign disks");
                foreach (var disk in disks)
                {
                    sb.AppendLine(disk.ClusterName);
                }
                MessageBox.Show(sb.ToString(), $"{disks.Count} Foreign Disks");
            }

            ForeignDisksCount.Content = disks.Count.ToString();
            if (disks.Count <= 0)
            {
                ForeignDisksCount.Foreground = System.Windows.Media.Brushes.Red;
                if (MessageBox.Show("No foreign disks were found, insert disks and re-launch the program.", "No new disks!", MessageBoxButton.OK) == MessageBoxResult.OK)
                {
                    System.Windows.Application.Current.Shutdown();
                }

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
                MessageBox.Show(sb.ToString(), $"{disks.Count} Broken Disks");

                BrokenDisksCount.Content = disks.Count.ToString();
                BrokenDisksCount.Foreground = System.Windows.Media.Brushes.Red;
            }
            BrokenDisksCount.Content = disks.Count.ToString();
            if (disks.Count <= 0)
            {
                BrokenDisksCount.Foreground = System.Windows.Media.Brushes.Green;
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
                    UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Error);
                    MessageBox.Show("Couldnt find cluster name of a disk in node mode! (disk show -m)", "Error!");
                    continue;
                }

                var matchingDisk = disks.FirstOrDefault(foreignDisk => foreignDisk.ClusterName == clusterName);

                if (matchingDisk == null)
                {
                    continue;
                }

                if (isPartition)
                {
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
                        var partitionOutput = ExecuteAndPrintOutput(client, $"disk remove_ownership {disk.NodeName}P{partition}", "Volumes must be taken offline", TimeSpan.FromSeconds(5));

                        if (partitionOutput != null)
                        {
                            ExecuteAndPrintOutput(client, $"y");
                        }
                    }
                }
                var output = ExecuteAndPrintOutput(client, $"disk remove_ownership {disk.NodeName}", "Volumes must be taken offline", TimeSpan.FromSeconds(5));

                if (output != null)
                {
                    ExecuteAndPrintOutput(client, $"y");
                }
            });
        }

        private List<Disk> GetForeignDisksFromDisks(List<Disk> allDisks, List<Node> nodes, List<Disk> unsupportedDisks)
        {
            return allDisks.Where(disk => !nodes.Exists(node => node.Name == disk.OwnerName) && disk.ContainerType != ContainerType.Broken && !unsupportedDisks.Exists(disk => disk.ClusterName == disk.ClusterName)).ToList();
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
                    NodeName = null,
                    Partitions = new List<int>()
                });
            }

            return disks;
        }

        private DiskType StringToDiskType(string v)
        {
            return v.ToLower() switch
            {
                "bsas" => DiskType.SATA,
                "msata" => DiskType.SATA,
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
                "unknown" => ContainerType.Unknown,
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
            var result = ExecuteAndPrintOutput(client, "node show -fields node, health");

            var nodesIndex = result.Substring(result.FindXOccurenceOfSubstring("\n", 2)).Trim();

            var lines = nodesIndex.Split("\n");

            var nodes = new List<Node>();

            for (int i = 0; i < lines.Length - 2; i++)
            {
                nodes.Add(new Node() { Name = lines[i].Split(";")[0], Health = lines[i].Split(";")[1].ToLower() == "true" });
            }

            return nodes;
        }

        private void PrintToSSHCommTextBlock(string output)
        {
            SSHCommTextBlock.Text = $"{SSHCommTextBlock.Text}{Environment.NewLine}{output}";
            SSHCommTextBlock.ScrollToEnd();
        }
        private string ExecuteAndPrintOutput(ShellStream client, string commandText, string textToExpect = ">", TimeSpan? timeout = null)
        {
            client.Write(commandText + "\n");

            string output;

            if (textToExpect != null)
            {
                client.Expect(commandText);

                output = timeout.HasValue ? client.Expect(textToExpect, timeout.Value) : client.Expect(textToExpect);
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

            var fixedOutput = output?.Trim();

            PrintToSSHCommTextBlock(fixedOutput);

            return fixedOutput;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IPAddress.Text = (string)Settings1.Default[settingsFileIpKey];
            Username.Text = (string)Settings1.Default[settingsFileUsernameKey];
            Password.Password = (string)Settings1.Default[settingsFilePasswordKey];
        }
        private void TerminateSSHSession(ShellStream stream, SshClient sshClient)
        {
            if (!areStreamsOpen)
            {
                return;
            }

            stream.Close();
            stream.Dispose();

            if (sshClient.IsConnected)
            {
                sshClient.Disconnect();
            }
            sshClient.Dispose();

            areStreamsOpen = false;

            Title = "Netapp Disk Cleaner";
        }

        private void AppenedTextToCurrentProgress(string textToAppend)
        {
            CurrentProgress.Text = textToAppend + Environment.NewLine + CurrentProgress.Text;
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { UpdateLayout(); }));
        }
        private void RemoveOwnerOfDisksClusterMode(ShellStream stream, List<Disk> disks)
        {
            foreach (var disk in disks)
            {
                var output = ExecuteAndPrintOutput(stream, $"disk removeowner {disk.ClusterName}");
                if (output.Contains("Background disk firmware update"))
                {
                    AppenedTextToCurrentProgress("Disk firmware update is in progress");
                    Thread.Sleep(65000);
                }
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

        private void DisksCheckBoxes_Checked_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            var checkbox = (CheckBox)sender;

            if (checkbox.Name == AllDisksCheckBox.Name && AllDisksCheckBox.IsChecked.Value)
            {
                UnownedDisksCheckBox.IsChecked = false;
                OwnedDisksCheckBox.IsChecked = false;
                BrokenDisksCheckBox.IsChecked = false;
                UnsupportedDisksCheckBox.IsChecked = false;
            }
            else if (checkbox.Name != AllDisksCheckBox.Name && checkbox.IsChecked.Value)
            {
                AllDisksCheckBox.IsChecked = false;
            }

            IEnumerable<Disk> tempFilteredDisks = _allDisks;

            tempFilteredDisks = tempFilteredDisks.Where(filteredDisk =>
            {
                bool doesPassFilter = false;

                if (AllDisksCheckBox.IsChecked.Value)
                {
                    doesPassFilter |= _allDisks.Any(disk => disk.ClusterName == filteredDisk.ClusterName);
                }
                if (UnownedDisksCheckBox.IsChecked.Value)
                {
                    doesPassFilter |= _foreignDisks.Any(disk => disk.ClusterName == filteredDisk.ClusterName);
                }
                if (OwnedDisksCheckBox.IsChecked.Value)
                {
                    doesPassFilter |= !_foreignDisks.Any(disk => disk.ClusterName == filteredDisk.ClusterName);
                }
                if (BrokenDisksCheckBox.IsChecked.Value)
                {
                    doesPassFilter |= _brokenDisks.Any(disk => disk.ClusterName == filteredDisk.ClusterName);
                }
                if (UnsupportedDisksCheckBox.IsChecked.Value)
                {
                    doesPassFilter |= _unsupportedDisks.Any(disk => disk.ClusterName == filteredDisk.ClusterName);
                }

                return doesPassFilter;
            });

            DisplayedDisks = tempFilteredDisks.ToList();
        }
        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTaskbarErrorState(1, System.Windows.Shell.TaskbarItemProgressState.Paused);
            MessageBoxResult executeConfirmationMessageBox = MessageBox.Show("This will start the cleanup process, are you sure you want to continue?", "Cleanup Confirmation", MessageBoxButton.YesNo);

            if (executeConfirmationMessageBox == MessageBoxResult.Yes)
            {
                var removeownerTime = TimeSpan.FromSeconds(_foreignDisks.Count * 3 * 5);

                UpdateTaskbarErrorState(0, System.Windows.Shell.TaskbarItemProgressState.Normal, "Running");

                var firstOwnedNode = nodes[0];

                AppenedTextToCurrentProgress("Entering node shell");
                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                taskbarInfo.ProgressValue = 0.1;

                AppenedTextToCurrentProgress("Retrieving disk info from (Node Shell)");
                EnrichDisksViaNodeShell(stream, _foreignDisks);

                taskbarInfo.ProgressValue = 0.2;

                AppenedTextToCurrentProgress($"Removing owner (Node Shell) this might take some time, maximum {removeownerTime.TotalSeconds} seconds.");
                RemoveOwnerOfDisksNodeMode(stream, _foreignDisks);

                taskbarInfo.ProgressValue = 0.5;

                AppenedTextToCurrentProgress("Exiting node shell");
                ExitNodeShell(stream);

                AppenedTextToCurrentProgress("Assigning disks to node (Cluster Shell)");
                AssignDisksToNode(stream, _foreignDisks, firstOwnedNode);

                taskbarInfo.ProgressValue = 0.6;

                AppenedTextToCurrentProgress("Entering node shell");
                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                AppenedTextToCurrentProgress("Assigning disks to node (Node Shell)");
                AssignDisksToNodeNodeMode(stream, _foreignDisks, firstOwnedNode);

                taskbarInfo.ProgressValue = 0.7;

                AppenedTextToCurrentProgress("Exiting node shell");
                ExitNodeShell(stream);

                AppenedTextToCurrentProgress("Retrieving foreign aggregates");
                var foreignAggregates = GetForeignAggregates(stream, aggregates).ToList();

                AppenedTextToCurrentProgress("Removing stale record");
                RemoveStaleRecordFromAggregates(stream, foreignAggregates);

                taskbarInfo.ProgressValue = 0.8;

                AppenedTextToCurrentProgress("Entering node shell");
                SetEnvironmentAndEnterNodeShell(stream, firstOwnedNode);

                AppenedTextToCurrentProgress("Unpartitioning disks");
                UnpartitionDisks(stream, _foreignDisks);

                taskbarInfo.ProgressValue = 0.85;

                AppenedTextToCurrentProgress("Exiting node shell");
                ExitNodeShell(stream);

                AppenedTextToCurrentProgress("Zeroing spares (if needed)");
                ZerospareDisksIfNeeded(stream, _foreignDisks);

                taskbarInfo.ProgressValue = 0.9;


                AppenedTextToCurrentProgress("Removing ownership from disk");
                RemoveOwnerOfDisksClusterMode(stream, _foreignDisks);

                taskbarInfo.ProgressValue = 1;

                TerminateSSHSession(stream, sshClient);

                AppenedTextToCurrentProgress("Finished!");
                MessageBox.Show($"Finished the cleanup for {_foreignDisks.Count} disks", "Finished!");

                UpdateTaskbarErrorState(0, System.Windows.Shell.TaskbarItemProgressState.None, null);

                SetButtonsState(true);


            }
        }

        private void CleanUI()
        {
            _allDisks = new List<Disk>();
            _foreignDisks = new List<Disk>();
            _brokenDisks = new List<Disk>();
            _displayedDisks = new List<Disk>();
            nodes = new List<Node>();
            aggregates = new List<Aggregate>();
            DisplayedDisks = new List<Disk>();
            ONTAPVersion.Content = "";
            AppenedTextToCurrentProgress("");
            SSHCommTextBlock.Clear();
            CurrentProgress.Clear();

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            TerminateSSHSession(stream, sshClient);
        }

    }
}
