using System;

namespace MSBuildProjectTools.LanguageServer
{
    using Utilities;

    /// <summary>
    ///     Represents a position in a text document that includes absolute positioning information.
    /// </summary>
    public class TextPosition
        : Position, IEquatable<TextPosition>, IComparable<TextPosition>
    {
        /// <summary>
        ///     Create a new <see cref="Position"/>.
        /// </summary>
        /// <param name="textPositions">
        ///     The <see cref="Utilities.TextPositions"/> that created the <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absolutePosition">
        ///     The (0-based) absolute position (number of characters from start of text).
        /// </param>
        /// <param name="lineNumber">
        ///     The line number (1-based).
        /// </param>
        /// <param name="columnNumber">
        ///     The column number (1-based).
        /// </param>
        internal TextPosition(TextPositions textPositions, int absolutePosition, int lineNumber, int columnNumber)
            : this(textPositions, absolutePosition, lineNumber, columnNumber, isZeroBased: false)
        {
        }

        /// <summary>
        ///     Create a new <see cref="Position"/>.
        /// </summary>
        /// <param name="textPositions">
        ///     The <see cref="Utilities.TextPositions"/> that created the <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absolutePosition">
        ///     The (0-based) absolute position (number of characters from start of text).
        /// </param>
        /// <param name="lineNumber">
        ///     The line number (1-based, unless <paramref name="isZeroBased"/> is <c>true</c>).
        /// </param>
        /// <param name="columnNumber">
        ///     The column number (1-based, unless <paramref name="isZeroBased"/> is <c>true</c>).
        /// </param>
        /// <param name="isZeroBased">
        ///     If true, then the position will be treated as 0-based.
        /// </param>
        TextPosition(TextPositions textPositions, int absolutePosition, int lineNumber, int columnNumber, bool isZeroBased)
            : base(lineNumber, columnNumber, isZeroBased)
        {
            if (textPositions == null)
                throw new ArgumentNullException(nameof(textPositions));

            if (absolutePosition < 0)
                throw new ArgumentOutOfRangeException(nameof(absolutePosition), absolutePosition, "Absolute position cannot be less than 0.");

            TextPositions = textPositions;
            AbsolutePosition = absolutePosition;
        }

        /// <summary>
        ///     The <see cref="Utilities.TextPositions"/> that created the <see cref="TextPosition"/>.
        /// </summary>
        public TextPositions TextPositions { get; }

        /// <summary>
        ///     The number of characters from the start of the text (always 0-based).
        /// </summary>
        public int AbsolutePosition { get; }

        /// <summary>
        ///     Create a copy of the <see cref="TextPosition"/> with the specified line number.
        /// </summary>
        /// <param name="lineNumber">
        ///     The new line number.
        /// </param>
        /// <returns>
        ///     The new <see cref="TextPosition"/>.
        /// </returns>
        public new TextPosition WithLineNumber(int lineNumber) => new TextPosition(TextPositions, AbsolutePosition, lineNumber, ColumnNumber, IsZeroBased);

        /// <summary>
        ///     Create a copy of the <see cref="TextPosition"/> with the specified column number.
        /// </summary>
        /// <param name="columnNumber">
        ///     The new column number.
        /// </param>
        /// <returns>
        ///     The new <see cref="TextPosition"/>.
        /// </returns>
        public new TextPosition WithColumnNumber(int columnNumber) => new TextPosition(TextPositions, AbsolutePosition, LineNumber, columnNumber, IsZeroBased);

        /// <summary>
        ///     Convert the position to a one-based position.
        /// </summary>
        /// <returns>
        ///     The converted position (or the existing position if it's already one-based).
        /// </returns>
        public new TextPosition ToOneBased() => IsZeroBased ? new TextPosition(TextPositions, AbsolutePosition, LineNumber + 1, ColumnNumber + 1, false) : this;

        /// <summary>
        ///     Convert the position to a zero-based position.
        /// </summary>
        /// <returns>
        ///     The converted position (or the existing position if it's already zero-based).
        /// </returns>
        public new TextPosition ToZeroBased() => IsOneBased ? new TextPosition(TextPositions, AbsolutePosition, LineNumber - 1, ColumnNumber - 1, true) : this;

        /// <summary>
        ///     Create a copy of the <see cref="TextPosition"/>, moving by the specified number characters.
        /// </summary>
        /// <param name="absoluteOffset">
        ///     The number of characters to move by.
        /// </param>
        /// <returns>
        ///     The new <see cref="TextPosition"/>.
        /// </returns>
        public TextPosition MoveAbsolute(int absoluteOffset)
        {
            if (absoluteOffset == 0)
                return this;

            TextPosition moved = TextPositions.GetPosition(AbsolutePosition + absoluteOffset);
            if (IsZeroBased)
                moved = moved.ToZeroBased();

            return moved;
        }

        /// <summary>
        ///     Create a copy of the <see cref="TextPosition"/>, moving by the specified number of lines and / or columns.
        /// </summary>
        /// <param name="lineCount">
        ///     The number of lines (if any) to move by.
        /// </param>
        /// <param name="columnCount">
        ///     The number of columns (if any) to move by.
        /// </param>
        /// <returns>
        ///     The new <see cref="Position"/>.
        /// </returns>
        public new TextPosition Move(int lineCount = 0, int columnCount = 0)
        {
            if (lineCount == 0 && columnCount == 0)
                return this;

            return new TextPosition(TextPositions, AbsolutePosition, LineNumber + lineCount, ColumnNumber + columnCount, IsZeroBased);
        }

        /// <summary>
        ///     Determine whether the position is equal to another object.
        /// </summary>
        /// <param name="other">
        ///     The other object.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the position and object are equal; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object other)
        {
            return Equals(other as Position);
        }

        /// <summary>
        ///     Get a hash code to represent the position.
        /// </summary>
        /// <returns>
        ///     The hash code.
        /// </returns>
        public override int GetHashCode()
        {
            int hashCode = 17;

            unchecked
            {
                hashCode += TextPositions.GetHashCode();
                hashCode *= 37;

                hashCode += AbsolutePosition.GetHashCode();
                hashCode *= 37;
            }

            return hashCode;
        }

        /// <summary>
        ///     Determine whether the position is equal to another position.
        /// </summary>
        /// <param name="other">
        ///     The other position.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the positions are equal; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool Equals(TextPosition other)
        {
            if (other == null)
                return false;

            return other.AbsolutePosition == AbsolutePosition && ReferenceEquals(other.TextPositions, TextPositions);
        }

        /// <summary>
        ///     Determine whether the position is equal to another position.
        /// </summary>
        /// <param name="other">
        ///     The other position.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the positions are equal; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(Position other)
        {
            if (other is TextPosition otherTextPosition)
                return Equals(otherTextPosition);

            return base.Equals(other);
        }

        /// <summary>
        ///     Compare the position to another position.
        /// </summary>
        /// <param name="other">
        ///     The other position.
        /// </param>
        /// <returns>
        ///     0 if the positions are equal, greater than 0 if the other position is less than the current position, less than 0 if the other position is greater than the current position.
        /// </returns>
        public virtual int CompareTo(TextPosition other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (!ReferenceEquals(other.TextPositions, TextPositions))
                throw new InvalidOperationException("Cannot compare a TextPosition with another TextPosition that comes from a different TextPositions.");

            return AbsolutePosition.CompareTo(other.AbsolutePosition);
        }

        /// <summary>
        ///     Compare the position to another position.
        /// </summary>
        /// <param name="other">
        ///     The other position.
        /// </param>
        /// <returns>
        ///     0 if the positions are equal, greater than 0 if the other position is less than the current position, less than 0 if the other position is greater than the current position.
        /// </returns>
        public override int CompareTo(Position other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (other is TextPosition otherTextPosition)
                return CompareTo(otherTextPosition);

            return base.CompareTo(other);
        }

        /// <summary>
        ///     Get a string representation of the position.
        /// </summary>
        /// <returns>
        ///     The string representation "LineNumber,ColumnNumber/AbsolutePosition".
        /// </returns>
        public override string ToString() => String.Format("{0},{1}/{2}", LineNumber, ColumnNumber, AbsolutePosition);

        /// <summary>
        ///     Create a new 0-based <see cref="Position"/>.
        /// </summary>
        /// <param name="textPositions">
        ///     The <see cref="Utilities.TextPositions"/> that is creating the <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absolutePosition">
        ///     The absolute position (0-based).
        /// </param>
        /// <param name="lineNumber">
        ///     The line number (0-based).
        /// </param>
        /// <param name="columnNumber">
        ///     The column number (0-based).
        /// </param>
        internal static TextPosition FromZeroBased(TextPositions textPositions, int absolutePosition, int lineNumber, int columnNumber)
        {
            return new TextPosition(textPositions, absolutePosition, lineNumber, columnNumber, isZeroBased: true);
        }

        /// <summary>
        ///     Create a new 0-based <see cref="TextPosition"/>.
        /// </summary>
        /// <param name="textPositions">
        ///     The <see cref="Utilities.TextPositions"/> that is creating the <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absolutePosition">
        ///     The absolute position (0-based).
        /// </param>
        /// <param name="lineNumber">
        ///     The line number (0-based).
        /// </param>
        /// <param name="columnNumber">
        ///     The column number (0-based).
        /// </param>
        public static TextPosition FromZeroBased(TextPositions textPositions, long absolutePosition, long lineNumber, long columnNumber)
        {
            // Seriously, who has an XML document with more lines or columns than you can fit in an Int32?
            return FromZeroBased(
                textPositions,
                (int)absolutePosition,
                (int)lineNumber,
                (int)columnNumber
            );
        }

        /// <summary>
        ///     Add an absolute offset to the <see cref="TextPosition"/>.
        /// </summary>
        /// <param name="textPosition">
        ///     The <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absoluteOffset">
        ///     The absolute offset.
        /// </param>
        /// <returns>
        ///     The new <see cref="TextPosition"/>.
        /// </returns>
        public static TextPosition operator +(TextPosition textPosition, int absoluteOffset)
        {
            if (textPosition == null)
                throw new ArgumentNullException(nameof(textPosition));

            return textPosition.MoveAbsolute(absoluteOffset);
        }

        /// <summary>
        ///     Subtract an absolute offset from the <see cref="TextPosition"/>.
        /// </summary>
        /// <param name="textPosition">
        ///     The <see cref="TextPosition"/>.
        /// </param>
        /// <param name="absoluteOffset">
        ///     The absolute offset.
        /// </param>
        /// <returns>
        ///     The new <see cref="TextPosition"/>.
        /// </returns>
        public static TextPosition operator -(TextPosition textPosition, int absoluteOffset)
        {
            if (textPosition == null)
                throw new ArgumentNullException(nameof(textPosition));

            return textPosition.MoveAbsolute(-absoluteOffset);
        }

        /// <summary>
        ///     Subtract 2 <see cref="TextPosition"/>s to calculate an absolute offset.
        /// </summary>
        /// <param name="textPosition1">
        ///     The first <see cref="TextPosition"/>.
        /// </param>
        /// <param name="textPosition2">
        ///     The second <see cref="TextPosition"/>.
        /// </param>
        /// <returns>
        ///     The number of characters between the first and second position.
        /// </returns>
        public static int operator -(TextPosition textPosition1, TextPosition textPosition2)
        {
            if (textPosition1 == null)
                throw new ArgumentNullException(nameof(textPosition1));
            
            if (textPosition2 == null)
                throw new ArgumentNullException(nameof(textPosition2));
            
            return textPosition1.AbsolutePosition - textPosition2.AbsolutePosition;
        }
    }
}
