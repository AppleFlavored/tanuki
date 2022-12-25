namespace Tanuki.Dom;

public sealed class Document : Node
{
    public override string Name => "#document";
    public override NodeType Type => NodeType.Document;

    public DocumentType? DocumentType => Children.FirstOrDefault() as DocumentType;

    public Document()
    {
    }
}