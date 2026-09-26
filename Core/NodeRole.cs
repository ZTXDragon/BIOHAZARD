namespace ZTX.BioCirculation.Core
{
    public enum NodeRole
    {
        Pipe,            // passive buffer, carries field
        PressureSource,  // heart: radial P_max
        ResetStation,    // lung/distributor: P_max on output ports
        Valve,           // directional reset + reverse block
        Sack,            // bigger-capacity buffer
        Sink,            // organs: drain volume
        OrganSource      // organs: inject volume
    }
}
