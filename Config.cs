using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace performance_metrics
{
    public class ConfigPlugin
    {
        public string Name;
        public string Version;
    }

    public class Config
    {
        public List<ulong> LastModIds = new List<ulong>();
        public List<SerializeableKeyValue<Guid, ConfigPlugin>> LastPlugins = new List<SerializeableKeyValue<Guid, ConfigPlugin>>(); 
    }

    public class SerializeableKeyValue<T1, T2>
    {
        public T1 Key { get; set; }
        public T2 Value { get; set; }
    }
}
