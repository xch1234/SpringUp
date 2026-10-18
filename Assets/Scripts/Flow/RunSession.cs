using Tideflesh.Systems;
namespace SpringUp.Flow
{
    // RunController remains the sole inventory/currency/wave authority.
    public sealed class RunSession
    {
        public RunController Run { get; }
        public float? Health { get; set; }
        public RunSession(RunConfiguration configuration)
        {
            Run = configuration.CreateRun();
            // Formal sessions stay locked until their scene host authorizes a laboratory interaction.
            Run.EquipmentAccessPolicy = () => false;
            Run.StartRun();
        }
    }
}
