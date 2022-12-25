namespace Tanuki.Dom;

public enum NodeType : ushort
{
    Element = 1,
    Attribute,
    Text,
    CData,
    EntityReference,
    Entity,
    ProcessingInstruction,
    Comment,
    Document,
    DocumentType,
    DocumentFragment,
    Notation
}