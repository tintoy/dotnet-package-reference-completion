using Microsoft.Language.Xml;
using System;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Represents non-significant whitespace (the syntax model refers to this as whitespace trivia).
    /// </summary>
    public class XSWhitespace
        : XSNode
    {
        /// <summary>
        ///     Create new <see cref="XSWhitespace"/>.
        /// </summary>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the whitespace.
        /// </param>
        /// <param name="parent">
        ///     The <see cref="XSElement"/> that contains the whitespace.
        /// </param>
        public XSWhitespace(TextRange range, XSElement parent)
            : base(range)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            
            Parent = parent;
        }

        /// <summary>
        ///     The kind of <see cref="XSNode"/>.
        /// </summary>
        public override XSNodeKind Kind => XSNodeKind.Whitespace;

        /// <summary>
        ///     The node name.
        /// </summary>
        public override string Name => "#Whitespace";

        /// <summary>
        ///     The <see cref="XSElement"/> that contains the whitespace.
        /// </summary>
        public XSElement Parent { get; }

        /// <summary>
        ///     Does the <see cref="XSNode"/> represent valid XML?
        /// </summary>
        public override bool IsValid => true;
    }
}
