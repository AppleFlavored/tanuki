namespace Tanuki.Dom;

public sealed class Element : Node
{
    public override string Name { get; }
    public override NodeType Type => NodeType.Element;

    public List<Attribute> Attributes { get; set; } = new();

    public Element(string name)
    {
        Name = name;
    }
}