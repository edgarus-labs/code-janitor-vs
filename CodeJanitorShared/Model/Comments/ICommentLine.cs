namespace CodeJanitor.Model.Comments
{
    internal interface ICommentLine
    {
        string Content { get; }

        bool IsLast { get; }
    }
}