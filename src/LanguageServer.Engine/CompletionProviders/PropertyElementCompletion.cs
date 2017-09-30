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
    ///     Completion provider for the common property elements.
    /// </summary>
    public class PropertyElementCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="PropertyElementCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public PropertyElementCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Property Elements";

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
                if (!location.CanCompleteElement(out replaceElement, asChildOfElementNamed: "PropertyGroup"))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not a direct child of a 'PropertyGroup' element).", location);

                    return null;
                }
                if (replaceElement == null)
                {
                    Log.Debug("Not offering any element completions for {XmlLocation:l} (no element to replace at this position).", location);

                    return null;
                }

                Log.Debug("Offering completions to replace element {ElementName} @ {ReplaceRange:l}",
                    replaceElement.Name,
                    replaceElement.Range
                );

                completions.AddRange(
                    GetCompletionItems(projectDocument, replaceElement.Range)
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
        ///     Get property element completions.
        /// </summary>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> for which completions will be offered.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(ProjectDocument projectDocument, Range replaceRange)
        {
            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            LspModels.Range replaceRangeLsp = replaceRange.ToLsp();

            HashSet<string> offeredPropertyNames = new HashSet<string>();

            // Special-case properties

            // Output type
            yield return new CompletionItem
            {
                Label = "<OutputType>",
                Detail = "Property",
                Documentation = MSBuildSchemaHelp.ForProperty("OutputType"),
                SortText = Priority + "<OutputType>",
                TextEdit = new TextEdit
                {
                    NewText = "<OutputType>${1|Library,Exe|}</OutputType>",
                    Range = replaceRangeLsp
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
            offeredPropertyNames.Add("OutputType");

            // Target framework
            yield return new CompletionItem
            {
                Label = "<TargetFramework>",
                Detail = "Property",
                Documentation = MSBuildSchemaHelp.ForProperty("TargetFramework"),
                SortText = Priority + "<TargetFramework>",
                TextEdit = new TextEdit
                {
                    NewText = "<TargetFramework>${1|netstandard1.0,netstandard1.1,netstandard1.2,netstandard1.3,netstandard1.4,netstandard1.5,netstandard1.6,netstandard2.0,netcoreapp1.0,netcoreapp1.1,netcoreapp2.0,net4,net451,net452,net46,net461,net462,net47|}</TargetFramework>",
                    Range = replaceRangeLsp
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
            offeredPropertyNames.Add("TargetFramework");

            // Well-known (but standard-format) properties.

            foreach (string wellKnownPropertyName in MSBuildSchemaHelp.WellKnownPropertyNames)
            {
                if (!offeredPropertyNames.Add(wellKnownPropertyName))
                    continue;

                yield return PropertyCompletionItem(wellKnownPropertyName, replaceRangeLsp,
                    description: MSBuildSchemaHelp.ForProperty(wellKnownPropertyName)
                );
            }
            
            if (!projectDocument.HasMSBuildProject)
                yield break; // Without a valid MSBuild project (even a cached one will do), we can't inspect existing MSBuild properties.

            if (!projectDocument.Workspace.Configuration.CompletionsFromProject.Contains(CompletionSource.Property))
                yield break;

            int otherPropertyPriority = Priority + 10;

            string[] otherPropertyNames =
                projectDocument.MSBuildProject.Properties
                    .Select(property => property.Name)
                    .Where(propertyName => !propertyName.StartsWith("_")) // Ignore private properties.
                    .ToArray();
            foreach (string propertyName in otherPropertyNames)
            {
                if (!offeredPropertyNames.Add(propertyName))
                    continue;

                yield return PropertyCompletionItem(propertyName, replaceRangeLsp, otherPropertyPriority,
                    description: $"I don't know anything about the '{propertyName}' property, but it's defined in this project (or a project that it imports); you can override its value by specifying it here."
                );
            }
        }

        /// <summary>
        ///     Create a standard <see cref="CompletionItem"/> for the specified MSBuild property.
        /// </summary>
        /// <param name="propertyName">
        ///     The MSBuild property name.
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
        CompletionItem PropertyCompletionItem(string propertyName, LspModels.Range replaceRange, int? priority = null, string description = null)
        {
            return new CompletionItem
            {
                Label = $"<{propertyName}>",
                Detail = "Property",
                Documentation = description,
                SortText = $"{priority ?? Priority:0000}<{propertyName}>",
                TextEdit = new TextEdit
                {
                    NewText = $"<{propertyName}>$0</{propertyName}>",
                    Range = replaceRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
        }
    }
}
