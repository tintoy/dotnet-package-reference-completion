using OmniSharp.Extensions.LanguageServer.Models;
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
    using Utilities;

    /// <summary>
    ///     Completion provider for the top-level elements (e.g. PropertyGroup, ItemGroup, Target, etc).
    /// </summary>
    public class TopLevelElementCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="TopLevelElementCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public TopLevelElementCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Top-level Elements";

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

            using (await projectDocument.ReaderLockAsync())
            {
                XSElement replaceElement;
                if (!location.CanCompleteElement(out replaceElement, asChildOfElementNamed: "Project"))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not a direct child of the 'Project' element).", location);

                    return null;
                }
                if (replaceElement == null)
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (no element to replace at this position).", location);

                    return null;
                }

                Log.Debug("Offering completions to replace element {ElementName} @ {ReplaceRange:l}",
                    replaceElement.Name,
                    replaceElement.Range
                );

                completions.AddRange(
                    GetCompletionItems(replaceElement.Range)
                );
            }

            Log.Debug("Offering {CompletionCount} completion(s) for {XmlLocation:l}", completions.Count, location);

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions,
                isIncomplete: false // Consider this list to be exhaustive
            );
        }

        /// <summary>
        ///     Get top-level element completions.
        /// </summary>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(Range replaceRange)
        {
            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            LspModels.Range completionRange = replaceRange.ToLsp();
            
            // <PropertyGroup>
            //     $0
            // </PropertyGroup>
            yield return new CompletionItem
            {
                Label = "<PropertyGroup>",
                Detail = "Element",
                Documentation = MSBuildSchemaHelp.ForElement("PropertyGroup"),
                SortText = Priority + "<PropertyGroup>",
                TextEdit = new TextEdit
                {
                    NewText = "<PropertyGroup>\n    $0\n</PropertyGroup>",
                    Range = completionRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };

            // <ItemGroup>
            //     $0
            // </ItemGroup>
            yield return new CompletionItem
            {
                Label = "<ItemGroup>",
                Detail = "Element",
                Documentation = MSBuildSchemaHelp.ForElement("ItemGroup"),
                SortText = Priority + "<ItemGroup>",
                TextEdit = new TextEdit
                {
                    NewText = "<ItemGroup>\n    $0\n</ItemGroup>",
                    Range = completionRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };

            // <Target Name="TargetName">
            //     $0
            // </Target>
            yield return new CompletionItem
            {
                Label = "<Target>",
                Detail = "Element",
                Documentation = MSBuildSchemaHelp.ForElement("Target"),
                SortText = Priority + "<Target>",
                TextEdit = new TextEdit
                {
                    NewText = "<Target Name=\"${1:TargetName}\">\n    $0\n</Target>",
                    Range = completionRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
        }
    }
}
