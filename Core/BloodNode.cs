namespace ZTX.BioCirculation.Core
{
    public sealed class BloodNode
    {
        public readonly int Id;
        public NodeRole Role;
        public int Volume;        // current blood units (inventory)
        public int Capacity;      // max blood units
        public int Decay;         // PressureDecay cost to enter this node (>=1 for pipes)
        public int PMax;          // reset target if this is a reset point; 0 otherwise
        public int Pressure;      // computed each field rebuild (static routing value)
        public int FlowRate = 4;  // max units this node may emit per SECOND, heart-buff scaled

        public int RateCredit;

        public int TickBudget;

        public int SplitCursor;

        public int Retain;

        public int Reserve;

        public int FlowRoom
        {
            get
            {
                int room = Capacity - Volume - Reserve;
                return room < 0 ? 0 : room;
            }
        }

        public bool PumpDriven;   // moved only by pumps; excluded from gradient flow entirely
        // Equalization model (biomass): moved only by DiffusionStep, excluded from the
        // pressure field and gradient flow entirely. See DiffusionStep for the model.
        public bool Diffusion;
        public bool PinPressureZero;
        public bool PropagatePressure = true;  // false = holds PMax for itself, paints nothing outward

        public BloodNode(int id, NodeRole role, int capacity, int decay)
        {
            Id = id; Role = role; Capacity = capacity; Decay = decay;
        }

        public BloodNode(int id, NodeRole role, int capacity, int decay, int flowRate)
            : this(id, role, capacity, decay)
        {
            FlowRate = flowRate;
        }
        public int RemainingCapacity => Capacity - Volume;
        public bool IsResetPoint => Role == NodeRole.PressureSource
                                 || Role == NodeRole.ResetStation
                                 || Role == NodeRole.Valve;
    }
}
