using System;
using System.Collections.Generic;
using System.Text;

namespace NetappDiskCleaner.Model
{
    public enum DiskType
    {
        DontCare,
        SAS,
        SSD,
    }

    public enum ContainerType
    {
        DontCare,
        Spare,
        Shared,
        Broken,
        Unassigned,
    }

    public class Disk
    {
        public string ClusterName { get; set; }
        public string NodeName { get; set; }
        public DiskType Type { get; set; }
        public ContainerType ContainerType { get; set; }
        public string OwnerName { get; set; }
        public List<int> Partitions { get; set; }
    }
}
