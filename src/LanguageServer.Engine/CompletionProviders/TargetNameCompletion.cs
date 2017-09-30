using OmniSharp.Extensions.LanguageServer.Models;
using Microsoft.Build.Execution;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
    ///     Completion provider for attributes on Target elements that refer to the names of other targets.
    /// </summary>
    public class TargetNameCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="TargetNameCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public TargetNameCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Target Name Attributes";

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

            using (await projectDocument.Lock.ReaderLockAsync())
            {
                XSAttribute attribute;
                if (!location.CanCompleteAttributeValue(out attribute, onElementNamed: "Target") || !SupportedAttributeNames.Contains(attribute.Name))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not the value of a supported attribute on a 'Target' element).", location);

                    return null;
                }

                Range targetRange = attribute.ValueRange;
                HashSet<string> excludeTargetNames = new HashSet<string>();

                // Handle potentially composite (i.e. "Value1;Value2;Value3") values, where it's legal to have them.
                if (attribute.Name != "Name" && attribute.Value.IndexOf(';') != -1)
                {
                    int startPosition = projectDocument.XmlPositions.GetAbsolutePosition(attribute.ValueRange.Start);
                    int relativePosition = location.AbsolutePosition - startPosition;

                    SimpleList list = MSBuildExpression.ParseSimpleList(attribute.Value);
                    SimpleListItem itemAtPosition = list.FindItemAt(relativePosition);
                    if (itemAtPosition == null)
                        return null;

                    Log.Debug("Completions will replace item {ItemValue} spanning [{ItemStartPosition}..{ItemEndPosition}) in MSBuild simple list expression {ListExpression}.",
                        itemAtPosition.Value,
                        itemAtPosition.AbsoluteStart,
                        itemAtPosition.AbsoluteEnd,
                        attribute.Value
                    );

                    targetRange = projectDocument.XmlPositions.GetRange(
                        absoluteStartPosition: startPosition + itemAtPosition.AbsoluteStart,
                        absoluteEndPosition: startPosition + itemAtPosition.AbsoluteEnd
                    );
                }

                completions.AddRange(
                    GetCompletionItems(projectDocument, targetRange, excludeTargetNames)
                );
            }

            if (completions.Count == 0)
                return null; // No completions provided.

            Log.Debug("Offering {CompletionCount} completion(s) for {XmlLocation:l}", completions.Count, location);

            return new CompletionList(completions,
                isIncomplete: false // Consider this list to be exhaustive
            );
        }

        /// <summary>
        ///     Get all completion items for target names.
        /// </summary>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> for which completions will be offered.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <param name="excludeTargetNames">
        ///     The names of targets (if any) to exclude from the completion list.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(ProjectDocument projectDocument, Range replaceRange, HashSet<string> excludeTargetNames)
        {
            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            HashSet<string> offeredTargetNames = new HashSet<string>(excludeTargetNames);
            LspModels.Range replaceRangeLsp = replaceRange.ToLsp();

            // Well-known targets.
            foreach (string targetName in WellKnownTargets.Keys)
            {
                if (!offeredTargetNames.Add(targetName))
                    continue;

                yield return TargetNameCompletionItem(targetName, replaceRangeLsp,
                    description: WellKnownTargets[targetName]
                );
            }

            // All other (public) targets defined in the project.

            if (!projectDocument.HasMSBuildProject)
                yield break; // Without a valid MSBuild project (even a cached one will do), we can't inspect existing MSBuild targets.

            if (!projectDocument.Workspace.Configuration.CompletionsFromProject.Contains(CompletionSource.Target))
                yield break;

            int otherTargetPriority = Priority + 10;
            ProjectTargetInstance[] otherTargets =
                projectDocument.MSBuildProject.Targets.Values
                    .Where(
                        target => !target.Name.StartsWith("_") // Ignore private targets.
                    )
                    .OrderBy(target => target.Name)
                    .ToArray();
            foreach (ProjectTargetInstance otherTarget in otherTargets)
            {
                if (!offeredTargetNames.Add(otherTarget.Name))
                    continue;

                // We can't really tell them much else about the target if it's not one of the well-known ones.
                string targetDescription = String.Format("Originally declared in {0} (line {1}, column {2})",
                    Path.GetFileName(otherTarget.Location.File),
                    otherTarget.Location.Line,
                    otherTarget.Location.Column
                );
                
                yield return TargetNameCompletionItem(otherTarget.Name, replaceRangeLsp, otherTargetPriority, targetDescription);
            }
        }

        /// <summary>
        ///     Create a standard <see cref="CompletionItem"/> for the specified MSBuild property.
        /// </summary>
        /// <param name="targetName">
        ///     The MSBuild target name.
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
        CompletionItem TargetNameCompletionItem(string targetName, LspModels.Range replaceRange, int? priority = null, string description = null)
        {
            return new CompletionItem
            {
                Label = targetName,
                Detail = "Target",
                Documentation = description,
                SortText = $"{priority ?? Priority:0000}{targetName}",
                TextEdit = new TextEdit
                {
                    NewText = targetName,
                    Range = replaceRange
                }
            };
        }

        /// <summary>
        ///     The names of attributes that the provider can complete.
        /// </summary>
        public static ImmutableHashSet<string> SupportedAttributeNames = 
            ImmutableHashSet.CreateRange(new string[]
            {
                "Name",
                "DependsOnTargets",
                "BeforeTargets",
                "AfterTargets"
            });

        /// <summary>
        ///     Well-known target descriptions, keyed by name.
        /// </summary>
        public static readonly ImmutableDictionary<string, string> WellKnownTargets =
            ImmutableDictionary.CreateRange(new Dictionary<string, string>
            {
                ["ResolveReferences"] = "Resolves all referenced assemblies, packages, and projects.",
                
                ["BeforeBuild"] = "A target guaranteed to run before the 'Build' target.",
                ["CoreCompile"] = "The main step when compiling the project source files.",
                ["CoreBuild"] = "The main step when building the project.",
                ["Build"] = "Build the project.\n\nNote that this is a big target with complex behaviour; if extending or referencing it from BeforeTargets / AfterTargets, you may be better off using BeforeBuild / CoreCompile / AfterBuild or a related target instead.",
                ["AfterBuild"] = "A target guaranteed to run after the 'Build' target.",
                
                ["BeforeClean"] = "A target guaranteed to run before the 'Clean' target.",
                ["CoreClean"] = "The main step when cleaning the project.",
                ["Clean"] = "Delete all intermediate and final build outputs.\n\nNote that this is a big target with complex behaviour; if extending or referencing it from BeforeTargets / AfterTargets, you may be better off using BeforeClean / CoreClean / AfterClean or a related target instead.",
                ["AfterClean"] = "A target guaranteed to run after the 'Clean' target.",
                
                ["BeforeRebuild"] = "A target guaranteed to run before the 'Rebuild' target.",
                ["CoreRebuild"] = "The main step when rebuilding the project.",
                ["Rebuild"] = "Delete all intermediate and final build outputs, and then build the project from scratch.\n\nNote that this is a big target with complex behaviour; if extending or referencing it from BeforeTargets / AfterTargets, you may be better off using BeforeRebuild / CoreRebuild / AfterRebuild or a related target instead.",
                ["AfterRebuild"] = "A target guaranteed to run after the 'Rebuild' target.",

                ["BeforePublish"] = "A target guaranteed to run before the 'Publish' target.",
                ["CorePublish"] = "The main step when publishing the project.",
                ["Publish"] = "Publish the project outputs.\n\nNote that this is a big target with complex behaviour; if extending or referencing it from BeforeTargets / AfterTargets, you may be better off using BeforePublish / CorePublish / AfterPublish or a related target instead.",
                ["AfterPublish"] = "A target guaranteed to run after the 'Publish' target."
            });
    }
}
