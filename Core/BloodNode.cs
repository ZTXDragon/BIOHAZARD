namespace ZTX.BioCirculation.Core
{
    public sealed class BloodNode
    {
        public readonly int Id;
        public NodeRole Role;
        public int Volume;
        public int Capacity;
        public int Decay;
        public int PMax;
        public int Pressure;
        public int FlowRate = 4;

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

        public bool PumpDriven;
        public bool Diffusion;
        public bool PinPressureZero;
        public bool PropagatePressure = true;

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
