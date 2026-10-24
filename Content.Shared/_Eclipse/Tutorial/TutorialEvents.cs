using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Tutorial;

/// <summary>
/// Client -> server: the player asked to start a tutorial lesson from the lobby.
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialStartRequestEvent : EntityEventArgs
{
    public string LessonId;

    public TutorialStartRequestEvent(string lessonId)
    {
        LessonId = lessonId;
    }
}
