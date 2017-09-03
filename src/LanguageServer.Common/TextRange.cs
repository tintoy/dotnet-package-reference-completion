using System;

namespace MSBuildProjectTools.LanguageServer
{
    using Utilities;

    /// <summary>
    ///     Represents a range in a text document with absolute position information.
    /// </summary>
    /// <remarks>
    ///     The range includes the start position, but does not (for the purposes of <see cref="Range.Contains(Position)"/> include the end position.
    /// </remarks>
    public class TextRange
        : Range
    {
        /// <summary>
        ///     Create a new <see cref="TextRange"/>.
        /// </summary>
        /// <param name="start">
        ///     The start position.
        /// </param>
        /// <param name="end">
        ///     The end position.
        /// </param>
        public TextRange(TextPosition start, TextPosition end)
            : base(start, end)
        {
            if (start == null)
                throw new ArgumentNullException(nameof(start));

            if (end == null)
                throw new ArgumentNullException(nameof(end));

            if (!ReferenceEquals(start.TextPositions, end.TextPositions))
                throw new ArgumentException("Cannot create a TextRange from an ending TextPosition that was created by a different TextPositions than its starting TextPosition.", nameof(end));
        }

        /// <summary>
        ///     The range's starting position.
        /// </summary>
        public new TextPosition Start => (TextPosition)base.Start;

        /// <summary>
        ///     The range's absolute starting position.
        /// </summary>
        public int AbsoluteStart => Start.AbsolutePosition;

        /// <summary>
        ///     The range's ending position.
        /// </summary>
        public new TextPosition End => (TextPosition)base.End;

        /// <summary>
        ///     The range's absolute ending position.
        /// </summary>
        public int AbsoluteEnd => End.AbsolutePosition;

        /// <summary>
        ///     The <see cref="Utilities.TextPositions"/> that created the <see cref="TextRange"/>'s positions.
        /// </summary>
        public TextPositions TextPositions => Start.TextPositions;

        /// <summary>
        ///     Create a copy of the <see cref="Range"/> with the specified starting position.
        /// </summary>
        /// <param name="start">
        ///     The new starting position.
        /// </param>
        /// <returns>
        ///     The new <see cref="Range"/>.
        /// </returns>
        public TextRange WithStart(TextPosition start) => new TextRange(start, End);

        /// <summary>
        ///     Create a copy of the <see cref="Range"/> with the specified ending position.
        /// </summary>
        /// <param name="end">
        ///     The new ending position.
        /// </param>
        /// <returns>
        ///     The new <see cref="Range"/>.
        /// </returns>
        public TextRange WithEnd(TextPosition end) => new TextRange(Start, end);

        /// <summary>
        ///     Transform the range by moving the start and end positions.
        /// </summary>
        /// <param name="moveStartLines">
        ///     The number of lines (if any) to move the start position.
        /// </param>
        /// <param name="moveStartColumns">
        ///     The number of columns (if any) to move the start position.
        /// </param>
        /// <param name="moveEndLines">
        ///     The number of lines (if any) to move the end position.
        /// </param>
        /// <param name="moveEndColumns">
        ///     The number of columns (if any) to move the start position.
        /// </param>
        /// <returns>
        ///     The number of columns (if any) to move the end position.
        /// </returns>
        public new TextRange Transform(int moveStartLines = 0, int moveStartColumns = 0, int moveEndLines = 0, int moveEndColumns = 0)
        {
            return new TextRange(
                Start.Move(moveStartLines, moveStartColumns),
                End.Move(moveEndLines, moveEndColumns)
            );
        }

        /// <summary>
        ///     Transform the range by moving the absolute start and end positions.
        /// </summary>
        /// <param name="moveStart">
        ///     The number of characters (if any) to move the start position.
        /// </param>
        /// <param name="moveEnd">
        ///     The number of lines (if any) to move the end position.
        /// </param>
        /// <returns>
        ///     The number of columns (if any) to move the end position.
        /// </returns>
        public TextRange TransformAbsolute(int moveStart = 0, int moveEnd = 0)
        {
            if (moveStart == 0 && moveEnd == 0)
                return this;

            return new TextRange(
                start: Start.MoveAbsolute(moveStart),
                end: End.MoveAbsolute(moveEnd)
            );
        }
    }
}
