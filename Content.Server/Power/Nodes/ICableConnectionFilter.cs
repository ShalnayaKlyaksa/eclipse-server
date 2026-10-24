namespace Content.Server.Power.Nodes;

public interface ICableConnectionFilter
{
    bool AllowsCable(EntityUid cable, IEntityManager entMan);
}
