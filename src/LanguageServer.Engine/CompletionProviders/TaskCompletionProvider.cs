using OmniSharp.Extensions.LanguageServer.Models;
using Microsoft.Build.Construction;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MSBuildProjectTools.LanguageServer.CompletionProviders
{
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Base class for MSBuild task completion providers.
    /// </summary>
    public abstract class TaskCompletionProvider
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="TaskCompletionProvider"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        protected TaskCompletionProvider(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     Get all tasks defined in the project.
        /// </summary>
        /// <param name="projectDocument">
        ///     The project document.
        /// </param>
        /// <returns>
        ///     A dictionary of task metadata, keyed by task name.
        /// </returns>
        protected async Task<Dictionary<string, MSBuildTaskMetadata>> GetProjectTasks(ProjectDocument projectDocument)
        {
            if (projectDocument == null)
                throw new ArgumentNullException(nameof(projectDocument));
            
            // We trust that all tasks discovered via GetMSBuildProjectTaskAssemblies are accessible in the current project.

            Dictionary<string, MSBuildTaskMetadata> tasks = new Dictionary<string, MSBuildTaskMetadata>();
            foreach (MSBuildTaskAssemblyMetadata assemblyMetadata in await projectDocument.GetMSBuildProjectTaskAssemblies())
            {
                foreach (MSBuildTaskMetadata task in assemblyMetadata.Tasks)
                    tasks[task.Name] = task;
            }

            return tasks;
        }
    }
}
