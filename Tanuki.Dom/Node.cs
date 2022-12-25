namespace Tanuki.Dom;

public abstract class Node
{
    public abstract string Name { get; }
    public abstract NodeType Type { get; }
    public virtual List<Node> Children { get; } = new();
}