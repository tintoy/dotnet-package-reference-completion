using Microsoft.Language.Xml;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Represents an invalid XML attribute.
    /// </summary>
    public class XSInvalidAttribute
        : XSAttribute
    {
        /// <summary>
        ///     Create a new <see cref="XSInvalidAttribute"/>.
        /// </summary>
        /// <param name="attribute">
        ///     The <see cref="XmlAttributeSyntax"/> represented by the <see cref="XSInvalidAttribute"/>.
        /// </param>
        /// <param name="element">
        ///     The element that contains the attribute.
        /// </param>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the attribute.
        /// </param>
        /// <param name="nameRange">
        ///     The <see cref="TextRange"/> spanned by the attribute's name.
        /// </param>
        /// <param name="valueRange">
        ///     The <see cref="TextRange"/> spanned by the attribute's value.
        /// </param>
        public XSInvalidAttribute(XmlAttributeSyntax attribute, XSElement element, TextRange range, TextRange nameRange, TextRange valueRange)
            : base(attribute, element, range, nameRange, valueRange)
        {
        }

        /// <summary>
        ///     Does the <see cref="XSNode"/> represent valid XML?
        /// </summary>
        public override bool IsValid => false;
    }
}
