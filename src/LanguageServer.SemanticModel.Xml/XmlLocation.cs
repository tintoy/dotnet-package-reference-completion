using Microsoft.Language.Xml;
using System;
using System.Diagnostics;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Information about a position in XML.
    /// </summary>
    [DebuggerDisplay("{ToString()}")]
    public class XmlLocation
    {
        /// <summary>
        ///     Create a new <see cref="XmlLocation"/>.
        /// </summary>
        /// <param name="position">
        ///     The location's position, in line / column form.
        /// </param>
        /// <param name="node">
        ///     The <see cref="XSNode"/> closest to the location's position.
        /// </param>
        /// <param name="flags">
        ///     <see cref="XmlLocationFlags"/> describing the location.
        /// </param>
        public XmlLocation(TextPosition position, XSNode node, XmlLocationFlags flags)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            if (node == null)
                throw new ArgumentNullException(nameof(node));
            
            Position = position;
            Node = node;
            Flags = flags;
        }

        /// <summary>
        ///     The position, in line / column form.
        /// </summary>
        public TextPosition Position { get; }

        /// <summary>
        ///     The (0-based) absolute position.
        /// </summary>
        public int AbsolutePosition => Position.AbsolutePosition;

        /// <summary>
        ///     The <see cref="XSNode"/> closest to the position.
        /// </summary>
        public XSNode Node { get; }
        
        /// <summary>
        ///     The node's parent node (if any).
        /// </summary>
        public XSNode Parent
        {
            get
            {
                switch (Node)
                {
                    case XSElement element:
                    {
                        return element.ParentElement;
                    }
                    case XSAttribute attribute:
                    {
                        return attribute.Element;
                    }
                    case XSElementText textContent:
                    {
                        return textContent.Element;
                    }
                    case XSWhitespace whitespace:
                    {
                        return whitespace.Parent;
                    }
                    default:
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        ///     The next sibling (if any) of the <see cref="XSNode"/> closest to the position.
        /// </summary>
        public XSNode NextSibling => Node?.NextSibling;

        /// <summary>
        ///     The previous sibling (if any) of the <see cref="XSNode"/> closest to the position.
        /// </summary>
        public XSNode PreviousSibling => Node?.PreviousSibling;

        /// <summary>
        ///     <see cref="XmlLocationFlags"/> value(s) describing the position.
        /// </summary>
        public XmlLocationFlags Flags { get; }

        /// <summary>
        ///     Get a string representation of the <see cref="XmlLocation"/>.
        /// </summary>
        /// <returns>
        ///     The display string.
        /// </returns>
        public override string ToString()
        {
            string nodeDescription = Node.Kind.ToString();
            if (Node is XSElement element)
                nodeDescription += $" '{element.Name}'";
            else if (Node is XSAttribute attribute)
                nodeDescription += $" '{attribute.Name}'";

            return String.Format("XmlPosition({0}) -> {1} @ {2}",
                Position,
                nodeDescription,
                Node.Range
            );
        }
    }
}
