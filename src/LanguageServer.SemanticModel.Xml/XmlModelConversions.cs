using Microsoft.Language.Xml;
using System;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    using Utilities;

    /// <summary>
    ///     Extension methods for converting models between native and third-party representations.
    /// </summary>
    public static class XmlModelConversions
    {
        /// <summary>
        ///     Convert the <see cref="TextSpan"/> to its native equivalent.
        /// </summary>
        /// <param name="span">
        ///     The <see cref="TextSpan"/> to convert.
        /// </param>
        /// <param name="textPositions">
        ///     The textual position lookup used to map absolute positions to lines and columns.
        /// </param>
        /// <returns>
        ///     The equivalent <see cref="Range"/>.
        /// </returns>
        public static TextRange ToNative(this TextSpan span, TextPositions textPositions)
        {
            if (textPositions == null)
                throw new ArgumentNullException(nameof(textPositions));

            TextPosition startPosition = textPositions.GetPosition(span.Start);
            TextPosition endPosition = textPositions.GetPosition(span.End);
            if (endPosition.ColumnNumber == 0)
                throw new InvalidOperationException("Should not happen anymore");

            return new TextRange(startPosition, endPosition);
        }
    }
}
