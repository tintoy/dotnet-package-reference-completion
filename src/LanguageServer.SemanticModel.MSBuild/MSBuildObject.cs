using Microsoft.Language.Xml;
using System;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     An object in an MSBuild project.
    /// </summary>
    public abstract class MSBuildObject
    {
        /// <summary>
        ///     Create a new <see cref="MSBuildObject"/>.
        /// </summary>
        /// <param name="xml">
        ///     A <see cref="SyntaxNode"/> representing the item's corresponding XML.
        /// </param>
        protected MSBuildObject(XSNode xml)
        {
            if (xml == null)
                throw new ArgumentNullException(nameof(xml));

            Xml = xml;
        }

        /// <summary>
        ///     A <see cref="SyntaxNode"/> representing the item's corresponding XML.
        /// </summary>
        public XSNode Xml { get; }

        /// <summary>
        ///     A <see cref="Range"/> representing the span of text covered by the item's XML.
        /// </summary>
        public TextRange XmlRange => Xml.Range;

        /// <summary>
        ///     The object's name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        ///     The kind of MSBuild object represented by the <see cref="MSBuildObject"/>.
        /// </summary>
        public abstract MSBuildObjectKind Kind { get; }

        /// <summary>
        ///     The full path of the file where the object is declared.
        /// </summary>
        public abstract string SourceFile { get; }

        /// <summary>
        ///     Determine whether another <see cref="MSBuildObject"/> represents the same underlying MSBuild object.
        /// </summary>
        /// <param name="other">
        ///     The <see cref="MSBuildObject"/>.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the 2 <see cref="MSBuildObject"/>s represent the same underlying MSBuild object; otherwise, <c>false</c>.
        /// </returns>
        public abstract bool IsSameUnderlyingObject(MSBuildObject other);

        /// <summary>
        ///     Determine whether the object's XML contains the specified position.
        /// </summary>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the the specified position lies within the object's XML span; otherwise, <c>false</c>.
        /// </returns>
        public bool XmlContains(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));
            
            return XmlRange.Contains(position);
        }
    }

    /// <summary>
    ///     An object of a known type in an MSBuild project.
    /// </summary>
    /// <typeparam name="TUnderlyingObject">
    ///     The type of underlying object represented by the <see cref="MSBuildObject{TUnderlyingObject}"/>.
    /// </typeparam>
    public abstract class MSBuildObject<TUnderlyingObject>
        : MSBuildObject
    {
        /// <summary>
        ///     Create a new <see cref="MSBuildObject{TUnderlyingObject}"/>.
        /// </summary>
        /// <param name="underlyingObject">
        ///     The underlying MSBuild object.
        /// </param>
        /// <param name="declaringXml">
        ///     A <see cref="SyntaxNode"/> representing the object's declaring XML.
        /// </param>
        protected MSBuildObject(TUnderlyingObject underlyingObject, XSNode declaringXml)
            : base(declaringXml)
        {
            if (underlyingObject == null)
                throw new ArgumentNullException(nameof(underlyingObject));
            
            UnderlyingObject = underlyingObject;
        }

        /// <summary>
        ///     The underlying MSBuild object.
        /// </summary>
        protected TUnderlyingObject UnderlyingObject { get; }

        /// <summary>
        ///     Determine whether another <see cref="MSBuildObject"/> represents the same underlying MSBuild object.
        /// </summary>
        /// <param name="other">
        ///     The <see cref="MSBuildObject"/>.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the 2 <see cref="MSBuildObject"/>s represent the same underlying MSBuild object; otherwise, <c>false</c>.
        /// </returns>
        public sealed override bool IsSameUnderlyingObject(MSBuildObject other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (other is MSBuildObject<TUnderlyingObject> otherWithUnderlying)
                return ReferenceEquals(UnderlyingObject, otherWithUnderlying.UnderlyingObject);

            return false;
        }
    }
}
