namespace performance_metrics
{
    public class Metric
    {
        public Process Process = new Process();
        public int ProgrammableBlocks;
        public int ProgrammableBlocksEnabled;
    }

    public class Process
    {
        public long PagedMemorySize;
        public long PrivateMemorySize;
        public long VirtualMemorySize;
    }
}
