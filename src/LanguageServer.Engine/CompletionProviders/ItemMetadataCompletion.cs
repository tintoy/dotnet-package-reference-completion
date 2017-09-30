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
    ///     Completion provider for metadata elements / attributes of items.
    /// </summary>
    public class ItemMetadataCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="ItemMetadataCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public ItemMetadataCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Item Metadata";

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

            Log.Debug("Evaluate completions for {XmlLocation:l}", location);

            List<CompletionItem> completions = new List<CompletionItem>();

            using (await projectDocument.ReaderLockAsync())
            {
                HashSet<string> existingMetadata = new HashSet<string>();
                
                completions.AddRange(
                    GetAttributeCompletions(location, projectDocument, existingMetadata)
                );

                completions.AddRange(
                    GetElementCompletions(location, projectDocument, existingMetadata)
                );
            }

            if (completions.Count == 0)
                return null; // No completions offered

            Log.Debug("Offering {CompletionCount} completions for {XmlLocation:l}.", location);

            return new CompletionList(completions,
                isIncomplete: false // List is exhaustive
            );
        }

        /// <summary>
        ///     Get completions for item attributes.
        /// </summary>
        /// <param name="location">
        ///     The <see cref="XmlLocation"/> where completions are requested.
        /// </param>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> that contains the <paramref name="location"/>.
        /// </param>
        /// <param name="existingMetadata">
        ///     Metadata already declared on the item.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        IEnumerable<CompletionItem> GetAttributeCompletions(XmlLocation location, ProjectDocument projectDocument, HashSet<string> existingMetadata)
        {
            Log.Debug("Evaluate attribute completions for {XmlLocation:l}", location);

            XSElement itemElement;
            XSAttribute replaceAttribute;
            PaddingType needsPadding;
            if (!location.CanCompleteAttribute(out itemElement, out replaceAttribute, out needsPadding))
            {
                Log.Debug("Not offering any attribute completions for {XmlLocation:l} (not a location where we can offer attribute completion.", location);

                yield break;
            }

            // Must be an item element.
            // TODO: Make an XmlLocation.IsItemElement extension method for this.
            if (itemElement.ParentElement?.Name != "ItemGroup")
            {
                Log.Debug("Not offering any attribute completions for {XmlLocation:l} (element is not a direct child of a 'PropertyGroup' element).", location);

                yield break;
            }

            string itemType = itemElement.Name;
            if (String.IsNullOrWhiteSpace(itemType))
            {
                Log.Debug("Not offering any attribute completions for {XmlLocation:l} (element represents a new, unnamed, item group).",
                    location,
                    itemType
                );

                yield break;
            }

            if (MSBuildSchemaHelp.ForItemType(itemType) == null)
            {
                Log.Debug("Not offering any attribute completions for {XmlLocation:l} ({ItemType} is not a well-known item type).",
                    location,
                    itemType
                );

                yield break;
            }

            Log.Debug("Will offer attribute completions for {XmlLocation:l} (padding: {NeedsPadding})", location, needsPadding);

            // Don't offer completions for existing metadata.
            existingMetadata.UnionWith(
                GetExistingMetadataNames(itemElement)
            );

            Range replaceRange = replaceAttribute?.Range ?? location.Position.ToEmptyRange();

            const string universalMetadataPrefix = "*.";
            string metadataPrefix = String.Format("{0}.", itemType);
            foreach (string metadataKey in MSBuildHelper.WellknownMetadataNames) // TODO: Find a better way to capture well-known item types / metadata names
            {
                string metadataName;
                if (metadataKey.StartsWith(metadataPrefix))
                    metadataName = metadataKey.Substring(metadataPrefix.Length);
                else if (metadataKey.StartsWith(universalMetadataPrefix))
                    metadataName = metadataKey.Substring(universalMetadataPrefix.Length);
                else
                    continue;

                if (existingMetadata.Contains(metadataName))
                    continue;

                if (MSBuildHelper.IsWellKnownItemMetadata(metadataName))
                    continue;

                yield return new CompletionItem
                {
                    Label = metadataName,
                    Detail = "Item Metadata",
                    Documentation = MSBuildSchemaHelp.ForItemMetadata(itemType, metadataName),
                    SortText = Priority + metadataName,
                    TextEdit = new TextEdit
                    {
                        NewText = $"{metadataName}=\"$0\"".WithPadding(needsPadding),
                        Range = replaceRange.ToLsp()
                    },
                    InsertTextFormat = InsertTextFormat.Snippet
                };
            }
        }

        /// <summary>
        ///     Get completions for item elements.
        /// </summary>
        /// <param name="location">
        ///     The <see cref="XmlLocation"/> where completions are requested.
        /// </param>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> that contains the <paramref name="location"/>.
        /// </param>
        /// <param name="existingMetadata">
        ///     Metadata already declared on the item.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        IEnumerable<CompletionItem> GetElementCompletions(XmlLocation location, ProjectDocument projectDocument, HashSet<string> existingMetadata)
        {
            Log.Debug("Evaluate element completions for {XmlLocation:l}", location);

            XSElement replaceElement;
            if (!location.CanCompleteElement(out replaceElement))
            {
                Log.Debug("Not offering any element completions for {XmlLocation:l} (not a location where an element can be created or replaced by completion).", location);

                yield break;
            }
            if (replaceElement == null)
            {
                Log.Debug("Not offering any element completions for {XmlLocation:l} (no element to replace at this position).", location);

                yield break;
            }

            if (replaceElement.ParentElement?.ParentElement?.Name != "ItemGroup")
            {
                Log.Debug("Not offering any element completions for {XmlLocation:l} (element's parent is not an item element).", location);

                yield break;
            }

            // These items are handled by PackageReferenceCompletion.
            if (replaceElement.ParentElement?.Name == "PackageReference" || replaceElement.ParentElement?.Name == "DotNetCliToolReference")
            {
                Log.Debug("Not offering any element completions for {XmlLocation:l} ({ItemType} items are handled by another provider).",
                    location,
                    replaceElement.Name
                );

                yield break;
            }

            string itemType = replaceElement.ParentElement.Name;
            if (MSBuildSchemaHelp.ForItemType(itemType) == null)
            {
                Log.Debug("Not offering any element completions for {XmlLocation:l} ({ItemType} is not a well-known item type).",
                    location,
                    itemType
                );

                yield break;
            }

            // Don't offer completions for existing metadata.
            existingMetadata.UnionWith(
                GetExistingMetadataNames(replaceElement)
            );

            Log.Debug("Will offer element completions for {XmlLocation:l}", location);

            const string universalMetadataPrefix = "*.";
            string metadataPrefix = String.Format("{0}.", itemType);
            foreach (string metadataKey in MSBuildHelper.WellknownMetadataNames) // TODO: Find a better way to capture well-known item types / metadata names
            {
                string metadataName;
                if (metadataKey.StartsWith(metadataPrefix))
                    metadataName = metadataKey.Substring(metadataPrefix.Length);
                else if (metadataKey.StartsWith(universalMetadataPrefix))
                    metadataName = metadataKey.Substring(universalMetadataPrefix.Length);
                else
                    continue;

                if (existingMetadata.Contains(metadataName))
                    continue;

                if (MSBuildHelper.IsWellKnownItemMetadata(metadataName))
                    continue;

                string completionLabel = $"<{metadataName}>";

                yield return new CompletionItem
                {
                    Label = completionLabel,
                    Detail = "Item Metadata",
                    Documentation = MSBuildSchemaHelp.ForItemMetadata(itemType, metadataName),
                    SortText = Priority + completionLabel,
                    TextEdit = new TextEdit
                    {
                        NewText = $"<{metadataName}>$0</{metadataName}>",
                        Range = replaceElement.Range.ToLsp()
                    },
                    InsertTextFormat = InsertTextFormat.Snippet
                };
            }
        }

        /// <summary>
        ///     Get the names of existing metadata on the target item.
        /// </summary>
        /// <param name="itemElement">
        ///     The item element.
        /// </param>
        /// <returns>
        ///     A sequence of existing metadata names.
        /// </returns>
        IEnumerable<string> GetExistingMetadataNames(XSElement itemElement)
        {
            if (itemElement == null)
                throw new ArgumentNullException(nameof(itemElement));
            
            foreach (XSAttribute metadataAttribute in itemElement.Attributes)
                yield return metadataAttribute.Name;

            foreach (XSElement metadataElement in itemElement.ChildElements)
                yield return metadataElement.Name;
        }
    }
}
