namespace Content.Server.Power.Nodes;

public interface IDirectionalCableNode
{
    bool ConnectsToCable(EntityUid cable, Direction direction, TransformComponent xform, IEntityManager entMan);
}
