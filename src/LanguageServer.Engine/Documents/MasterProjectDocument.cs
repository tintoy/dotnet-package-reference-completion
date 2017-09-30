using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Language.Xml;
using Nito.AsyncEx;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MSBuildProjectTools.LanguageServer.Documents
{
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Represents the document state for an MSBuild project.
    /// </summary>
    public class MasterProjectDocument
        : ProjectDocument
    {
        /// <summary>
        ///     Sub-projects (if any).
        /// </summary>
        Dictionary<Uri, SubProjectDocument> _subProjects = new Dictionary<Uri, SubProjectDocument>();

        /// <summary>
        ///     Create a new <see cref="MasterProjectDocument"/>.
        /// </summary>
        /// <param name="workspace">
        ///     The document workspace.
        /// </param>
        /// <param name="documentUri">
        ///     The document URI.
        /// </param>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public MasterProjectDocument(Workspace workspace, Uri documentUri, ILogger logger)
            : base(workspace, documentUri, logger)
        {
        }

        /// <summary>
        ///     Dispose of resources being used by the <see cref="ProjectDocument"/>.
        /// </summary>
        /// <param name="disposing">
        ///     Explicit disposal?
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (MSBuildProjectCollection != null)
                {
                    MSBuildProjectCollection.Dispose();
                    MSBuildProjectCollection = null;
                }
            }
        }

        /// <summary>
        ///     Sub-projects (if any).
        /// </summary>
        public IReadOnlyDictionary<Uri, SubProjectDocument> SubProjects => _subProjects;

        /// <summary>
        ///     Add a sub-project.
        /// </summary>
        /// <param name="subProjectDocument">
        ///     The sub-project.
        /// </param>
        public void AddSubProject(SubProjectDocument subProjectDocument)
        {
            if (subProjectDocument == null)
                throw new ArgumentNullException(nameof(subProjectDocument));

            _subProjects.Add(subProjectDocument.DocumentUri, subProjectDocument);
        }

        /// <summary>
        ///     Remove a sub-project.
        /// </summary>
        /// <param name="documentUri">
        ///     The sub-project document URI.
        /// </param>
        public void RemoveSubProject(Uri documentUri)
        {
            if (documentUri == null)
                throw new ArgumentNullException(nameof(documentUri));
            
            SubProjectDocument subProjectDocument;
            if (!_subProjects.TryGetValue(documentUri, out subProjectDocument))
                return;

            subProjectDocument.Unload();
            _subProjects.Remove(documentUri);
        }

        /// <summary>
        ///     Unload the project.
        /// </summary>
        public override void Unload()
        {
            // Unload sub-projects, if necessary.
            Uri[] subProjectDocumentUris = SubProjects.Keys.ToArray();
            foreach (Uri subProjectDocumentUri in subProjectDocumentUris)
                RemoveSubProject(subProjectDocumentUri);

            base.Unload();
        }

        /// <summary>
        ///     Load the project document.
        /// </summary>
        /// <param name="cancellationToken">
        ///     A cancellation token that can be used to cancel the load.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        public override async Task Load(CancellationToken cancellationToken)
        {
            await base.Load(cancellationToken);

            if (!Workspace.Configuration.NuGet.DisablePreFetch)
                WarmUpNuGetClient();
        }

        /// <summary>
        ///     Attempt to load the underlying MSBuild project.
        /// </summary>
        /// <returns>
        ///     <c>true</c>, if the project was successfully loaded; otherwise, <c>false</c>.
        /// </returns>
        protected override bool TryLoadMSBuildProject()
        {
            try
            {
                if (HasMSBuildProject && !IsDirty)
                    return true;

                if (MSBuildProjectCollection == null)
                    MSBuildProjectCollection = MSBuildHelper.CreateProjectCollection(ProjectFile.Directory.FullName);

                if (HasMSBuildProject && IsDirty)
                {
                    using (StringReader reader = new StringReader(Xml.ToFullString()))
                    using (XmlTextReader xmlReader = new XmlTextReader(reader))
                    {
                        MSBuildProject.Xml.ReloadFrom(xmlReader,
                            throwIfUnsavedChanges: false,
                            preserveFormatting: true
                        );
                    }

                    MSBuildProject.ReevaluateIfNecessary();

                    Log.Debug("Successfully updated MSBuild project '{ProjectFileName}' from in-memory changes.");
                }
                else
                    MSBuildProject = MSBuildProjectCollection.LoadProject(ProjectFile.FullName);

                return true;
            }
            catch (InvalidProjectFileException invalidProjectFile)
            {
                AddErrorDiagnostic(invalidProjectFile.BaseMessage,
                    range: invalidProjectFile.GetRange(),
                    diagnosticCode: invalidProjectFile.ErrorCode
                );
            }
            catch (XmlException invalidProjectXml)
            {
                // TODO: Match SourceUri (need overloads of AddXXXDiagnostic for reporting diagnostics for other files).
                AddErrorDiagnostic(invalidProjectXml.Message,
                    range: invalidProjectXml.GetRange(),
                    diagnosticCode: "MSBuild.InvalidXML"
                );
            }
            catch (Exception loadError)
            {
                Log.Error(loadError, "Error loading MSBuild project '{ProjectFileName}'.", ProjectFile.FullName);
            }

            return false;
        }

        /// <summary>
        ///     Attempt to unload the underlying MSBuild project.
        /// </summary>
        /// <returns>
        ///     <c>true</c>, if the project was successfully unloaded; otherwise, <c>false</c>.
        /// </returns>
        protected override bool TryUnloadMSBuildProject()
        {
            try
            {
                if (!HasMSBuildProject)
                    return true;

                if (MSBuildProjectCollection == null)
                    return true;

                MSBuildProjectCollection.UnloadProject(MSBuildProject);
                MSBuildProject = null;

                return true;
            }
            catch (Exception unloadError)
            {
                Log.Error(unloadError, "Error unloading MSBuild project '{ProjectFileName}'.", ProjectFile.FullName);

                return false;
            }
        }

        /// <summary>
        ///     Warm up the project's NuGet client.
        /// </summary>
        void WarmUpNuGetClient()
        {
            SuggestPackageIds("Newtonsoft.Json").ContinueWith(task =>
            {
                Log.Debug(task.Exception.Flatten().InnerExceptions[0],
                     "Error initialising NuGet client."
                );
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
