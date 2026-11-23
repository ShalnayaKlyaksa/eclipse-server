using Robust.Shared.Serialization;

namespace Content.Shared.Eclipse.News;

[Serializable, NetSerializable]
public enum ForumAction : byte
{
    List,
    Read,
    Ask,
    Reply,
    Delete,
}

[Serializable, NetSerializable]
public sealed record ForumPost(int Id, string Author, string Body, DateTime PostedAt);

[Serializable, NetSerializable]
public sealed record ForumQuestion(int Id, string Title, string Author, DateTime PostedAt, int AnswerCount);

[Serializable, NetSerializable]
public sealed class ForumRequest : EntityEventArgs
{
    public const int TitleLimit = 96;
    public const int BodyLimit = 1600;

    public ForumAction Action;
    public int RequestId;
    public int TopicId;
    public int PostId;
    public int Page;
    public string? Title;
    public string? Body;
}

[Serializable, NetSerializable]
public sealed class ForumResponse : EntityEventArgs
{
    public int RequestId;
    public int Page;
    public int PageCount;
    public bool CanModerate;
    public string? Error;
    public ForumQuestion[] Questions = [];
    public ForumQuestion? Topic;
    public ForumPost? Question;
    public ForumPost[] Answers = [];
}
