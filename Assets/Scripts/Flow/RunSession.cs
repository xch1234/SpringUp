using Tideflesh.Systems;
namespace SpringUp.Flow
{
    // RunController remains the sole inventory/currency/wave authority.
    public sealed class RunSession
    {
        public RunController Run { get; }
        public float? Health { get; set; }
        public float Charge { get; set; }
        public RunSession(RunConfiguration configuration)
        {
            Run = configuration.CreateRun();
            Run.StartRun();
        }
    }
}
