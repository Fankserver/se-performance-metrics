using Torch;
using Torch.API;

namespace performance_metrics
{
    public class PerformanceMetricsPlugin : TorchPluginBase
    {
        public override void Init(ITorchBase torch)
        {
            var pgmr = new PerformanceMetricsManager(torch);
            torch.Managers.AddManager(pgmr);
        }
    }
}
