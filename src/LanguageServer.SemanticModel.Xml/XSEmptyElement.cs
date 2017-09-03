using Microsoft.Language.Xml;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Represents an empty XML element.
    /// </summary>
    public class XSEmptyElement
        : XSElement
    {
        /// <summary>
        ///     Create a new <see cref="XSEmptyElement"/>.
        /// </summary>
        /// <param name="emptyElement">
        ///     The <see cref="XmlEmptyElementSyntax"/> represented by the <see cref="XSEmptyElement"/>.
        /// </param>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the node.
        /// </param>
        /// <param name="attributesRange">
        ///     The range, within the source text, spanned by the element's attributes.
        /// </param>
        /// <param name="parent">
        ///     The <see cref="XSEmptyElement"/>'s parent element (if any).
        /// </param>
        public XSEmptyElement(XmlEmptyElementSyntax emptyElement, TextRange range, TextRange attributesRange, XSElement parent)
            : base(emptyElement, range, attributesRange, parent)
        {
        }

        /// <summary>
        ///     The <see cref="XmlEmptyElementSyntax"/> represented by the <see cref="XSEmptyElement"/>.
        /// </summary>
        public new XmlEmptyElementSyntax ElementNode => (XmlEmptyElementSyntax)SyntaxNode;

        /// <summary>
        ///     The kind of XML node represented by the <see cref="XSNode"/>.
        /// </summary>
        public override XSNodeKind Kind => XSNodeKind.Element;

        /// <summary>
        ///     Does the <see cref="XSNode"/> represent valid XML?
        /// </summary>
        public override bool IsValid => true;

        /// <summary>
        ///     Does the <see cref="XSElement"/> have any content (besides attributes)?
        /// </summary>
        public override bool HasContent => false;
    }
}
