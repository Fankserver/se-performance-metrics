using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace performance_metrics
{
    public class Metric
    {
        public Process Process = new Process();
        public List<ProgrammableBlocks> ProgrammableBlocks  = new List<ProgrammableBlocks>();
    }

    public class Process
    {
        public long PagedMemorySize;
        public long PrivateMemorySize;
        public long VirtualMemorySize;
    }

    public class ProgrammableBlocks
    {
        public bool Enabled;
    }
}
