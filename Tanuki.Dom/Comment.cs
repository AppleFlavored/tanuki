namespace Tanuki.Dom;

public sealed class Comment : Node
{
    public override string Name => "";
    public override NodeType Type => NodeType.Comment;
    public override List<Node> Children => null;

    public string Data { get; }
    
    public Comment(string data)
    {
        Data = data;
    }
}