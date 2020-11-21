using System;
using System.Collections.Generic;
using System.Text;

namespace NetappDiskCleaner.Model
{
    public class Aggregate
    {
        public string Name { get; set; }
        public string OwnerNode { get; set; }
        public bool IsOnline { get; set; }
    }
}
