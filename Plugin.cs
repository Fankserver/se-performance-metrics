using NLog;
using System.IO;
using Torch;
using Torch.API;

namespace performance_metrics
{
    public class PerformanceMetricsPlugin : TorchPluginBase
    {
        private Persistent<Config> _config;

        public override void Init(ITorchBase torch)
        {
            string path = Path.Combine(StoragePath, "performance_metrics.cfg");
            LogManager.GetCurrentClassLogger().Info($"Attempting to load config from {path}");
            _config = Persistent<Config>.Load(path);

            var pgmr = new PerformanceMetricsManager(torch, _config);
            torch.Managers.AddManager(pgmr);
        }
    }
}
