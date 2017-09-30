using OmniSharp.Extensions.LanguageServer.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MSBuildProjectTools.LanguageServer.CompletionProviders
{
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Completion provider for attributes of items.
    /// </summary>
    public class ItemAttributeCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     The names of well-known attributes for MSBuild item elements.
        /// </summary>
        public static readonly ImmutableHashSet<string> WellKnownItemAttributes =
            ImmutableHashSet.CreateRange(new string[]
            {
                "Include",
                "Condition",
                "Exclude",
                "Update"
            });

        /// <summary>
        ///     Create a new <see cref="ItemAttributeCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public ItemAttributeCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Item Attributes";

        /// <summary>
        ///     Provide completions for the specified location.
        /// </summary>
        /// <param name="location">
        ///     The <see cref="XmlLocation"/> where completions are requested.
        /// </param>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> that contains the <paramref name="location"/>.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A <see cref="Task{TResult}"/> that resolves either a <see cref="CompletionList"/>s, or <c>null</c> if no completions are provided.
        /// </returns>
        public override async Task<CompletionList> ProvideCompletions(XmlLocation location, ProjectDocument projectDocument, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (location == null)
                throw new ArgumentNullException(nameof(location));

            if (projectDocument == null)
                throw new ArgumentNullException(nameof(projectDocument));

            List<CompletionItem> completions = new List<CompletionItem>();

            using (await projectDocument.ReaderLockAsync())
            {
                XSElement element;
                XSAttribute replaceAttribute;
                PaddingType needsPadding;
                if (!location.CanCompleteAttribute(out element, out replaceAttribute, out needsPadding))
                    return null;

                // Must be an item element.
                // TODO: Make an XmlLocation.IsItemElement extension method for this.
                if (element.ParentElement?.Name != "ItemGroup")
                    return null;

                Range replaceRange = replaceAttribute?.Range ?? location.Position.ToEmptyRange();
                
                completions.AddRange(
                    WellKnownItemAttributes.Except(
                        element.AttributeNames
                    )
                    .Select(attributeName => new CompletionItem
                    {
                        Label = attributeName,
                        Detail = "Attribute",
                        Kind = CompletionItemKind.Field,
                        TextEdit = new TextEdit
                        {
                            NewText = $"{attributeName}=\"$1\"$0".WithPadding(needsPadding),
                            Range = replaceRange.ToLsp()
                        },
                        InsertTextFormat = InsertTextFormat.Snippet
                    })
                );
            }

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions, isIncomplete: false);
        }
    }
}
