using OmniSharp.Extensions.LanguageServer.Models;
using Microsoft.Build.Construction;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
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
    ///     Completion provider for the MSBuild task attributes.
    /// </summary>
    public class TaskParameterCompletion
        : TaskCompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="TaskParameterCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public TaskParameterCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Task Attributes";

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
                Log.Debug("Not offering task attribute completions for {XmlLocation:l} (task completions not enabled in extension settings).", location);

                return null;
            }

            if (!projectDocument.HasMSBuildProject)
            {
                Log.Debug("Not offering task attribute completions for {XmlLocation:l} (underlying MSBuild project is not loaded).", location);

                return null;
            }

            List<CompletionItem> completions = new List<CompletionItem>();

            Log.Debug("Evaluate completions for {XmlLocation:l}", location);

            using (await projectDocument.Lock.ReaderLockAsync())
            {
                XSElement taskElement;
                XSAttribute replaceAttribute;
                PaddingType needsPadding;
                if (!location.CanCompleteAttribute(out taskElement, out replaceAttribute, out needsPadding))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (not a location an attribute can be created or replaced by completion).", location);

                    return null;
                }

                if (String.IsNullOrWhiteSpace(taskElement.Name))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (location represents an empty element).", location);

                    return null;
                }

                if (taskElement.ParentElement?.Name != "Target")
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (attribute is not on an element that's a direct child of a 'Target' element).", location);

                    return null;
                }

                Dictionary<string, MSBuildTaskMetadata> projectTasks = await GetProjectTasks(projectDocument);
                MSBuildTaskMetadata taskMetadata;
                if (!projectTasks.TryGetValue(taskElement.Name, out taskMetadata))
                {
                    Log.Debug("Not offering any completions for {XmlLocation:l} (no metadata available for task {TaskName}).", location, taskElement.Name);

                    return null;
                }

                Range replaceRange = replaceAttribute?.Range ?? location.Position.ToEmptyRange();
                if (replaceAttribute != null)
                {
                    Log.Debug("Offering completions to replace attribute {AttributeName} @ {ReplaceRange:l}",
                        replaceAttribute.Name,
                        replaceRange
                    );
                }
                else
                {
                    Log.Debug("Offering completions to create attribute @ {ReplaceRange:l}",
                        replaceRange
                    );
                }

                HashSet<string> existingAttributeNames = new HashSet<string>(
                    taskElement.AttributeNames
                );
                if (replaceAttribute != null)
                    existingAttributeNames.Remove(replaceAttribute.Name);

                completions.AddRange(
                    GetCompletionItems(projectDocument, taskMetadata, existingAttributeNames, replaceRange, needsPadding)
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
        ///     Get task attribute completions.
        /// </summary>
        /// <param name="projectDocument">
        ///     The <see cref="ProjectDocument"/> for which completions will be offered.
        /// </param>
        /// <param name="taskMetadata">
        ///     Metadata for the task whose parameters are being offered as completions.
        /// </param>
        /// <param name="existingAttributeNames">
        ///     Existing parameter names (if any) declared on the element.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text to be replaced by the completions.
        /// </param>
        /// <param name="needsPadding">
        ///     The type of padding (if any) required.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="CompletionItem"/>s.
        /// </returns>
        public IEnumerable<CompletionItem> GetCompletionItems(ProjectDocument projectDocument, MSBuildTaskMetadata taskMetadata, HashSet<string> existingAttributeNames, Range replaceRange, PaddingType needsPadding)
        {
            if (replaceRange == null)
                throw new ArgumentNullException(nameof(replaceRange));

            LspModels.Range replaceRangeLsp = replaceRange.ToLsp();

            foreach (MSBuildTaskParameterMetadata taskParameter in taskMetadata.Parameters.OrderBy(parameter => parameter.Name))
            {
                if (existingAttributeNames.Contains(taskParameter.Name))
                    continue;

                if (taskParameter.IsOutput)
                    continue;

                string parameterDocumentation = MSBuildSchemaHelp.ForTaskParameter(taskMetadata.Name, taskParameter.Name);

                yield return TaskParameterCompletionItem(taskMetadata.Name, taskParameter, parameterDocumentation, replaceRangeLsp, needsPadding);
            }
        }

        /// <summary>
        ///     Create a <see cref="CompletionItem"/> for the specified MSBuild task parameter.
        /// </summary>
        /// <param name="taskName">
        ///     The MSBuild task name.
        /// </param>
        /// <param name="parameterMetadata">
        ///     The MSBuild task's metadata.
        /// </param>
        /// <param name="parameterDocumentation">
        ///     Documentation (if available) for the task parameter.
        /// </param>
        /// <param name="replaceRange">
        ///     The range of text that will be replaced by the completion.
        /// </param>
        /// <param name="needsPadding">
        ///     The type of padding (if any) required.
        /// </param>
        /// <returns>
        ///     The <see cref="CompletionItem"/>.
        /// </returns>
        CompletionItem TaskParameterCompletionItem(string taskName, MSBuildTaskParameterMetadata parameterMetadata, string parameterDocumentation, LspModels.Range replaceRange, PaddingType needsPadding)
        {
            return new CompletionItem
            {
                Label = parameterMetadata.Name,
                Detail = "Task Parameter",
                Documentation = parameterDocumentation,
                SortText = $"{Priority:0000}parameterMetadata.Name",
                TextEdit = new TextEdit
                {
                    NewText = $"{parameterMetadata.Name}=\"$1\"".WithPadding(needsPadding),
                    Range = replaceRange
                },
                InsertTextFormat = InsertTextFormat.Snippet
            };
        }
    }
}
