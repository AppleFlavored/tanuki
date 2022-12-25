namespace Tanuki.Dom;

public sealed class Text : Node
{
    public override string Name => "#text";
    public override NodeType Type => NodeType.Text;
    public override List<Node> Children => null;
    
    public string Data { get; set; }

    public Text(string data)
    {
        Data = data;
    }
}