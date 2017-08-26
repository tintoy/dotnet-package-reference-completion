using System;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Flags describing a position in the XML.
    /// </summary>
    [Flags]
    public enum XmlPositionFlags
    {
        /// <summary>
        ///     No flags.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Position is on an element.
        /// </summary>
        Element = 1,

        /// <summary>
        ///     Position is on the opening tag of an element.
        /// </summary>
        OpeningTag = 2,

        /// <summary>
        ///     Position is on the closing tag of an element.
        /// </summary>
        ClosingTag = 4,

        /// <summary>
        ///     Position is on an attribute.
        /// </summary>
        Attribute = 8,

        /// <summary>
        ///     Position is on a name.
        /// </summary>
        Name = 16,

        /// <summary>
        ///     Position is on element content.
        /// </summary>
        ElementContent = 32,

        /// <summary>
        ///     Position is on element attributes.
        /// </summary>
        ElementAttributes = 64,

        /// <summary>
        ///     Position is on attribute content.
        /// </summary>
        AttributeContent = 128,

        /// <summary>
        ///     Element or attribute has no content.
        /// </summary>
        Empty = 256
    }
}