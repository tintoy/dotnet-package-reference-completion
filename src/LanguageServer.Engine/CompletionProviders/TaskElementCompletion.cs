using OmniSharp.Extensions.LanguageServer.Models;
using Microsoft.Build.Construction;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LspModels = OmniSharp.Extensions.LanguageServer.Models;

namespace MSBuildProjectTools.LanguageServer.CompletionProviders
{
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Completion provider for the MSBuild task elements.
    /// </summary>
    public class TaskElementCompletion
        : TaskCompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="TaskElementCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public TaskElementCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Task Elements";

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

            if (!projectDocument.Workspace.Configuration.CompletionsFromProject.Contains(CompletionSource.Task))
            {
                Log.Debug("Not offering task element completions for {XmlLocation:l} (task completions not enabled in extension settings).", location);

                return null;
            }

            if (!projectDocument.HasMSBuildProject)
            {
                Log.Debug("Not offering task element completions for {XmlLocation:l} (underlying MSBuild project is not loaded).", location);

                return null;
            }

            List<CompletionItem> completions = new List<CompletionItem>();

            Log.Debug("Evaluate completions for {XmlLocation:l}", location);

            using (await projectDocument.Lock.ReaderLockAsync())
            {
                XSElement replaceElement;
                if (!location.CanCompleteElement(out replaceElement, asChildOfElementNamed: "Target"))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (does not represent the direct child of a 'Target' element).", location);

                    return null;
                }
                
                if (replaceElement != null)
                {
                    Log.Debug("Offering completions to replace element {ElementName} @ {ReplaceRange:l}",
                        replaceElement.Name,
                        replaceElement.Range
                    );

                    Dictionary<string, MSBuildTaskMetadata> projectTasks = await GetProjectTasks(projectDocument);

                    completions.AddRange(
                        GetCompletionItems(projectDocument, projectTasks, replaceElement.Range)
                    );
                }
                else
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (no element to replace at this position).", location);

                    return null;
                }
            }

            Log.Debug("Offering {CompletionCount} completion(s) for {XmlLocation:l}", completions.Count, location);

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions,
                isIncomplete: false // Consider this list to be exhaustive
            );
        }

        /// <summary>
        ///     Get task element completions.
        /// </summary>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> for which completions will be offered.
        /// </param>
        /// <param name="projectTasks">
        ///     The metadata for the tasks defined in the project (and its imports), keyed by task name.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(ProjectDocument projectDocument, Dictionary<string, MSBuildTaskMetadata> projectTasks, Range replaceRange)
        {
            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            LspModels.Range replaceRangeLsp = replaceRange.ToLsp();

            foreach (string taskName in projectTasks.Keys.OrderBy(name => name))
                yield return TaskElementCompletionItem(taskName, projectTasks[taskName], replaceRangeLsp);

            // TODO: Offer task names for inline and assembly-name-based tasks.
        }

        /// <summary>
        ///     Create a <see cref="CompletionItem"/> for the specified MSBuild task element.
        /// </summary>
        /// <param name="taskName">
        ///     The MSBuild task name.
        /// </param>
        /// <param name="taskMetadata">
        ///     The MSBuild task's metadata.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text that will be replaced by the completion.
        /// </param>
        /// <returns>
        ///     The <see cref="CompletionItem"/>.
        /// </returns>
        CompletionItem TaskElementCompletionItem(string taskName, MSBuildTaskMetadata taskMetadata, LspModels.Range replaceRange)
        {
            MSBuildTaskParameterMetadata[] requiredParameters = taskMetadata.Parameters.Where(parameter => parameter.IsRequired).ToArray();
            string requiredAttributes = String.Join(" ", requiredParameters.Select(
                (parameter, index) => $"{parameter.Name}=\"${index + 1}\""
            ));
            string attributePadding = (requiredAttributes.Length > 0) ? " " : String.Empty;

            string restOfElement = " />$0";
            if (taskMetadata.Parameters.Any(parameter => parameter.IsOutput))
            {
                // Create Outputs sub-element if there are any output parameters.
                restOfElement = $">\n    ${requiredParameters.Length + 1}\n</{taskName}>$0";
            }
            
            return new CompletionItem
            {
                Label = $"<{taskName}>",
                Detail = "Task",
                Documentation = MSBuildSchemaHelp.ForTask(taskName),
                SortText = $"{Priority:0000}<{taskName}>",
                TextEdit = new TextEdit
                {
                    NewText = $"<{taskName}{attributePadding}{requiredAttributes}{restOfElement}",
                    Range = replaceRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
        }
    }
}
