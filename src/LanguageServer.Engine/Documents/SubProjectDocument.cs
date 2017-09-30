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
    ///     Represents the document state for an MSBuild sub-project.
    /// </summary>
    public class SubProjectDocument
        : ProjectDocument
    {
        /// <summary>
        ///     Create a new <see cref="SubProjectDocument"/>.
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
        /// <param name="masterProjectDocument">
        ///     The master project document that owns the sub-project.
        /// </param>
        public SubProjectDocument(Workspace workspace, Uri documentUri, ILogger logger, MasterProjectDocument masterProjectDocument)
            : base(workspace, documentUri, logger)
        {
            if (masterProjectDocument == null)
                throw new ArgumentNullException(nameof(masterProjectDocument));
            
            MasterProjectDocument = masterProjectDocument;
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
                MSBuildProjectCollection = null;
        }

        /// <summary>
        ///     A <see cref="ProjectDocument"/> representing the project's parent (i.e. main) project.
        /// </summary>
        public ProjectDocument MasterProjectDocument { get; }

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

                if (!MasterProjectDocument.HasMSBuildProject)
                    throw new InvalidOperationException("Parent project does not have an MSBuild project.");

                if (MSBuildProjectCollection == null)
                    MSBuildProjectCollection = MasterProjectDocument.MSBuildProjectCollection;

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
    }
}
