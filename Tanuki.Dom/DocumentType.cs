namespace Tanuki.Dom;

public sealed class DocumentType : Node
{
    public override string Name { get; }
    public override NodeType Type => NodeType.DocumentType;

    public string PublicId { get; }
    public string SystemId { get; }
    
    public DocumentType(string name, string publicId, string systemId)
    {
        Name = name;
        PublicId = publicId;
        SystemId = systemId;
    }
}