using Microsoft.Language.Xml;
using System;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Represents text within an XML element's content.
    /// </summary>
    public class XSElementText
        : XSNode<XmlTextSyntax>
    {
        /// <summary>
        ///     Create new <see cref="XSElementText"/>.
        /// </summary>
        /// <param name="textNode">
        ///     The <see cref="XmlTextSyntax"/> represented by the <see cref="XSElementText"/>.
        /// </param>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the text.
        /// </param>
        /// <param name="element">
        ///     The element whose content includes the text.
        /// </param>
        public XSElementText(XmlTextSyntax textNode, TextRange range, XSElement element)
            : base(textNode, range)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            
            Element = element;
        }

        /// <summary>
        ///     The <see cref="XmlTextSyntax"/> represented by the <see cref="XSElementText"/>.
        /// </summary>
        public XmlTextSyntax TextNode => SyntaxNode;

        /// <summary>
        ///     The text.
        /// </summary>
        public string Text => TextNode.ToFullString();

        /// <summary>
        ///     The element whose content includes the text.
        /// </summary>
        public XSElement Element { get; }

        /// <summary>
        ///     The kind of XML node represented by the <see cref="XSNode"/>.
        /// </summary>
        public override XSNodeKind Kind => XSNodeKind.Text;

        /// <summary>
        ///     The node name.
        /// </summary>
        public override string Name => "#Text";

        /// <summary>
        ///     Does the <see cref="XSNode"/> represent valid XML?
        /// </summary>
        public override bool IsValid => true;
    }
}
