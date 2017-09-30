using OmniSharp.Extensions.LanguageServer.Models;
using Microsoft.Build.Evaluation;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using LspModels = OmniSharp.Extensions.LanguageServer.Models;

namespace MSBuildProjectTools.LanguageServer.CompletionProviders
{
    using Documents;
    using SemanticModel;
    using SemanticModel.MSBuildExpressions;
    using Utilities;

    /// <summary>
    ///     Completion provider for item metadata expressions.
    /// </summary>
    public class ItemMetadataExpressionCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="ItemMetadataExpressionCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public ItemMetadataExpressionCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "ItemMetadata Expressions";

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

            Log.Debug("Evaluate completions for {XmlLocation:l}", location);

            using (await projectDocument.Lock.ReaderLockAsync())
            {
                if (!projectDocument.EnableExpressions)
                    return null;

                ExpressionNode expression;
                Range expressionRange;
                if (!location.IsExpression(out expression, out expressionRange))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not on an expression or a location where an expression can be added).", location);

                    return null;
                }

                // AF: This code is getting complicated; refactor, please.

                if (expression is Symbol)
                    expression = expression.Parent; // The containing expression.

                bool offerItemTypes = true; // By default, we offer item types.
                string targetItemType = null;
                if (expression is ExpressionTree)
                    expressionRange = location.Position.ToEmptyRange(); // We're between expressions, so just insert.
                else if (expression is ItemMetadata metadataExpression && metadataExpression.HasItemType)
                {
                    targetItemType = metadataExpression.ItemType;
                    offerItemTypes = false; // We don't offer item types if one is already specified in the metadata expression.
                }
                
                bool offerUnqualifiedCompletions = false;
                if (location.IsAttribute(out XSAttribute attribute) && attribute.Element.ParentElement?.Name == "ItemGroup")
                {
                    // An attribute on an item element.
                    targetItemType = targetItemType ?? attribute.Element.Name;
                    offerUnqualifiedCompletions = true;
                }
                else if (location.IsElementText(out XSElementText elementText) && elementText.Element.ParentElement?.Name == "ItemGroup")
                {
                    // Text inside a an item element's metadata element.
                    offerUnqualifiedCompletions = true;
                    targetItemType = targetItemType ?? elementText.Element.Name;
                }
                else if (location.IsWhitespace(out XSWhitespace whitespace) && whitespace.ParentElement?.ParentElement?.Name == "ItemGroup")
                {
                    // Whitespace inside a an item element's metadata element.
                    offerUnqualifiedCompletions = true;
                    targetItemType = targetItemType ?? whitespace.ParentElement.Name;
                }
                else
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not a location where metadata expressions are supported).", location);

                    return null;
                }
                
                Log.Debug("Offering completions to replace ItemMetadata expression @ {ReplaceRange:l} (OfferItemTypes={OfferItemTypes}, OfferUnqualifiedCompletions={OfferUnqualifiedCompletions})",
                    expressionRange,
                    offerItemTypes,
                    offerUnqualifiedCompletions
                );

                completions.AddRange(
                    GetCompletionItems(projectDocument, expressionRange, targetItemType, offerItemTypes, offerUnqualifiedCompletions)
                );
            }

            Log.Debug("Offering {CompletionCount} completion(s) for {XmlLocation:l}.", completions.Count, location);

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions,
                isIncomplete: true // We need to be re-queried to catch the switch from unqualified to qualified metadata.
            );
        }

        /// <summary>
        ///     Get item metadata element completions.
        /// </summary>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> for which completions will be offered.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <param name="targetItemType">
        ///     The target item type (if known) for the metadata expression being completed.
        /// </param>
        /// <param name="offerItemTypes">
        ///     Offer completions for item types?
        /// </param>
        /// <param name="offerUnqualifiedCompletions">
        ///     Offer completions for unqualified metadata expressions?
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(ProjectDocument projectDocument, Range replaceRange, string targetItemType, bool offerItemTypes, bool offerUnqualifiedCompletions)
        {
            if (projectDocument == null)
                throw new ArgumentNullException(nameof(projectDocument));

            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            LspModels.Range replaceRangeLsp = replaceRange.ToLsp();

            int priority = Priority;

            // Built-in metadata (unqualified).
            if (offerUnqualifiedCompletions)
            {
                foreach (string wellknownMetadataName in MSBuildHelper.WellknownMetadataNames)
                {
                    yield return UnqualifiedMetadataCompletionItem(wellknownMetadataName, replaceRangeLsp, priority,
                        description: MSBuildSchemaHelp.ForItemMetadata("*", wellknownMetadataName)
                    );
                }
            }

            // We can't go any further if we can't inspect project state.
            if (!projectDocument.HasMSBuildProject)
                yield break;

            
            if (!String.IsNullOrWhiteSpace(targetItemType))
            {
                priority += 100;
                
                SortedSet<string> metadataNames = new SortedSet<string>(
                    projectDocument.MSBuildProject.GetItems(targetItemType)
                        .SelectMany(
                            item => item.Metadata.Select(metadata => metadata.Name)
                        )
                        .Where(
                            metadataName => !MSBuildHelper.IsWellKnownItemMetadata(metadataName) && !MSBuildHelper.IsPrivateMetadata(metadataName)
                        )
                );

                priority += 100;

                // Metadata for the current item type (unqualified).
                if (offerUnqualifiedCompletions)
                {
                    foreach (string metadataName in metadataNames)
                    {
                        yield return UnqualifiedMetadataCompletionItem(metadataName, replaceRangeLsp, priority,
                            description: MSBuildSchemaHelp.ForItemMetadata(targetItemType, metadataName) ?? "Item metadata declared in this project."
                        );
                    }
                }

                priority += 100;

                // Well-known item metadata (qualified).
                foreach (string wellknownMetadataName in MSBuildHelper.WellknownMetadataNames)
                {
                    yield return QualifiedMetadataCompletionItem(targetItemType, wellknownMetadataName, replaceRangeLsp, priority,
                        description: MSBuildSchemaHelp.ForItemMetadata("*", wellknownMetadataName)
                    );
                }

                priority += 100;

                // Metadata for the current item type (qualified).
                foreach (string metadataName in metadataNames)
                {
                    yield return QualifiedMetadataCompletionItem(targetItemType, metadataName, replaceRangeLsp, priority,
                        description: MSBuildSchemaHelp.ForItemMetadata(targetItemType, metadataName) ?? "Item metadata declared in this project."
                    );
                }
            }

            if (offerItemTypes)
            {
                priority += 100;

                // Item types defined in the project.
                foreach (string itemType in projectDocument.MSBuildProject.ItemTypes)
                {
                    if (MSBuildHelper.IsPrivateItemType(itemType))
                        continue;

                    yield return ItemTypeCompletionItem(itemType, replaceRangeLsp, priority,
                        description: MSBuildSchemaHelp.ForItemType(itemType) ?? "Item type declared in this project."
                    );
                }
            }
        }

        /// <summary>
        ///     Create a standard <see cref="CompletionItem"/> for the specified MSBuild item type.
        /// </summary>
        /// <param name="itemType">
        ///     The MSBuild item metadata name.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text that will be replaced by the completion.
        /// </param>
        /// <param name="priority">
        ///     The item sort priority (defaults to <see cref="CompletionProvider.Priority"/>).
        /// </param>
        /// <param name="description">
        ///     An optional description for the item.
        /// </param>
        /// <returns>
        ///     The <see cref="CompletionItem"/>.
        /// </returns>
        CompletionItem ItemTypeCompletionItem(string itemType, LspModels.Range replaceRange, int? priority = null, string description = null)
        {
            return new CompletionItem
            {
                Label = $"%({itemType})",
                Kind = CompletionItemKind.Class,
                Detail = "Item Group",
                Documentation = description,
                FilterText = $"%({itemType}.)", // Trailing "." ensures the user can type "." to switch to qualified item metadata expression without breaking completion.
                SortText = $"{priority ?? Priority:0000}%({itemType})",
                TextEdit = new TextEdit
                {
                    NewText = $"%({itemType}.)",
                    Range = replaceRange
                },
                Command = new Command
                {
                    // Move back inside the parentheses so they can continue completion.
                    Name = "msbuildProjectTools.internal.moveAndSuggest",
                    Arguments = new object[]
                    {
                        "left",      // moveTo
                        "character", // moveBy
                        1            // moveCount
                    }
                }
            };
        }

        /// <summary>
        ///     Create a standard <see cref="CompletionItem"/> for the specified unqualified MSBuild item metadata.
        /// </summary>
        /// <param name="metadataName">
        ///     The MSBuild item metadata name.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text that will be replaced by the completion.
        /// </param>
        /// <param name="priority">
        ///     The item sort priority (defaults to <see cref="CompletionProvider.Priority"/>).
        /// </param>
        /// <param name="description">
        ///     An optional description for the item.
        /// </param>
        /// <returns>
        ///     The <see cref="CompletionItem"/>.
        /// </returns>
        CompletionItem UnqualifiedMetadataCompletionItem(string metadataName, LspModels.Range replaceRange, int? priority = null, string description = null)
        {
            return new CompletionItem
            {
                Label = $"%({metadataName})",
                Detail = "Item Metadata",
                Kind = CompletionItemKind.Field,
                Documentation = description,
                FilterText = $"%({metadataName})",
                SortText = $"{priority ?? Priority:0000}%({metadataName})",
                TextEdit = new TextEdit
                {
                    NewText = $"%({metadataName})",
                    Range = replaceRange
                }
            };
        }

        /// <summary>
        ///     Create a standard <see cref="CompletionItem"/> for the specified qualified MSBuild item metadata.
        /// </summary>
        /// <param name="itemType">
        ///     The MSBuild item type.
        /// </param>
        /// <param name="metadataName">
        ///     The MSBuild item metadata name.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text that will be replaced by the completion.
        /// </param>
        /// <param name="priority">
        ///     The item sort priority (defaults to <see cref="CompletionProvider.Priority"/>).
        /// </param>
        /// <param name="description">
        ///     An optional description for the item.
        /// </param>
        /// <returns>
        ///     The <see cref="CompletionItem"/>.
        /// </returns>
        CompletionItem QualifiedMetadataCompletionItem(string itemType, string metadataName, LspModels.Range replaceRange, int? priority = null, string description = null)
        {
            return new CompletionItem
            {
                Label = $"%({itemType}.{metadataName})",
                Detail = "Item Metadata",
                Documentation = description,
                Kind = CompletionItemKind.Property,
                SortText = $"{priority ?? Priority:0000}%({itemType}.{metadataName})",
                TextEdit = new TextEdit
                {
                    NewText = $"%({itemType}.{metadataName})",
                    Range = replaceRange
                }
            };
        }
    }
}
