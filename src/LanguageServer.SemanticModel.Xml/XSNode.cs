using Microsoft.Language.Xml;
using System;

using MLXML = Microsoft.Language.Xml;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Represents an XML node in the semantic model.
    /// </summary>
    public abstract class XSNode
    {
        // TODO: Consider storing TextPositions here to allow XSNode and friends to calculate Positions and Ranges as-needed.

        /// <summary>
        ///     Create a new <see cref="XSNode"/>.
        /// </summary>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the node.
        /// </param>
        protected XSNode(TextRange range)
        {
            if (range == null)
                throw new ArgumentNullException(nameof(range));
            
            Range = range;
        }

        /// <summary>
        ///     The <see cref="TextRange"/> spanned by the node.
        /// </summary>
        public TextRange Range { get; }

        /// <summary>
        ///     The node's starting position.
        /// </summary>
        public Position Start => Range.Start;

        /// <summary>
        ///     The node's ending position.
        /// </summary>
        public Position End => Range.End;

        /// <summary>
        ///     The node's next sibling node (if any).
        /// </summary>
        public XSNode NextSibling { get; internal set; }

        /// <summary>
        ///     The node's previous sibling node (if any).
        /// </summary>
        public XSNode PreviousSibling { get; internal set; }

        /// <summary>
        ///     The kind of XML node represented by the <see cref="XSNode"/>.
        /// </summary>
        public abstract XSNodeKind Kind { get; }

        /// <summary>
        ///     The node name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        ///     Does the <see cref="XSNode"/> represent valid XML?
        /// </summary>
        public abstract bool IsValid { get; }
    }

    /// <summary>
    ///     Represents an XML node in the semantic model with a known type of corresponding <see cref="MLXML.SyntaxNode"/>.
    /// </summary>
    /// <typeparam name="TSyntax">
    ///     The type of <see cref="MLXML.SyntaxNode"/> represented by the <see cref="XSNode{TSyntax}"/>.
    /// </typeparam>
    public abstract class XSNode<TSyntax>
        : XSNode
        where TSyntax : SyntaxNode
    {
        /// <summary>
        ///     Create a new <see cref="XSNode{TSyntax}"/>.
        /// </summary>
        /// <param name="syntaxNode">
        ///     The <typeparamref name="TSyntax"/> represented by the <see cref="XSNode{TSyntax}"/>.
        /// </param>
        /// <param name="range">
        ///     The <see cref="TextRange"/> spanned by the node.
        /// </param>
        protected XSNode(TSyntax syntaxNode, TextRange range)
            : base(range)
        {
            if (syntaxNode == null)
                throw new ArgumentNullException(nameof(syntaxNode));
            
            SyntaxNode = syntaxNode;
        }

        /// <summary>
        ///     The underlying <typeparamref name="TSyntax"/> represented by the <see cref="XSNode{TSyntax}"/>.
        /// </summary>
        protected TSyntax SyntaxNode { get; }
    }
}
