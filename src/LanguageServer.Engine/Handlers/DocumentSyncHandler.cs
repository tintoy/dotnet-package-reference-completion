using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Abstractions;
using OmniSharp.Extensions.LanguageServer.Capabilities.Client;
using OmniSharp.Extensions.LanguageServer.Capabilities.Server;
using OmniSharp.Extensions.LanguageServer.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using NuGet.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.IO;
using OmniSharp.Extensions.JsonRpc;

namespace MSBuildProjectTools.LanguageServer.Handlers
{
    using CustomProtocol;
    using Documents;
    using SemanticModel;
    using Serilog.Events;
    using Utilities;

    /// <summary>
    ///     The handler for language server document synchronisation.
    /// </summary>
    public sealed class DocumentSyncHandler
        : Handler, ITextDocumentSyncHandler
    {
        /// <summary>
        ///     Create a new <see cref="DocumentSyncHandler"/>.
        /// </summary>
        /// <param name="server">
        ///     The language server.
        /// </param>
        /// <param name="workspace">
        ///     The document workspace.
        /// </param>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public DocumentSyncHandler(ILanguageServer server, Workspace workspace, ILogger logger)
            : base(server, logger)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            Workspace = workspace;
        }

        /// <summary>
        ///     Options that control synchronisation.
        /// </summary>
        public TextDocumentSyncOptions Options { get; } = new TextDocumentSyncOptions
        {
            WillSaveWaitUntil = false,
            WillSave = true,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions
            {
                IncludeText = true
            },
            OpenClose = true
        };

        /// <summary>
        ///     The document selector that describes documents to synchronise.
        /// </summary>
        DocumentSelector DocumentSelector { get; } = new DocumentSelector(
            new DocumentFilter
            {
                Pattern = "**/*.*",
                Language = "msbuild"
            },
            new DocumentFilter
            {
                Pattern = "**/*.*proj",
                Language = "xml"
            },
            new DocumentFilter
            {
                Pattern = "**/*.props",
                Language = "xml"
            },
            new DocumentFilter
            {
                Pattern = "**/*.targets",
                Language = "xml"
            }
        );

        /// <summary>
        ///     The document workspace.
        /// </summary>
        Workspace Workspace { get; }

        /// <summary>
        ///     Has the client supplied synchronisation capabilities?
        /// </summary>
        bool HaveSynchronizationCapabilities => SynchronizationCapabilities != null;

        /// <summary>
        ///     The client's synchronisation capabilities.
        /// </summary>
        SynchronizationCapability SynchronizationCapabilities { get; set; }

        /// <summary>
        ///     Get registration options for handling document events.
        /// </summary>
        TextDocumentRegistrationOptions DocumentRegistrationOptions
        {
            get => new TextDocumentRegistrationOptions
            {
                DocumentSelector = DocumentSelector
            };
        }

        /// <summary>
        ///     Get registration options for handling document-change events.
        /// </summary>
        TextDocumentChangeRegistrationOptions DocumentChangeRegistrationOptions
        {
            get => new TextDocumentChangeRegistrationOptions
            {
                DocumentSelector = DocumentSelector,
                SyncKind = Options.Change
            };
        }

        /// <summary>
        ///     Get registration options for handling document save events.
        /// </summary>
        TextDocumentSaveRegistrationOptions DocumentSaveRegistrationOptions
        {
            get => new TextDocumentSaveRegistrationOptions
            {
                DocumentSelector = DocumentSelector,
                IncludeText = Options.Save.IncludeText
            };
        }

        /// <summary>
        ///     Called when a text document is opened.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task OnDidOpenTextDocument(DidOpenTextDocumentParams parameters)
        {
            Server.NotifyBusy("Loading project...");

            ProjectDocument projectDocument = await Workspace.GetProjectDocument(parameters.TextDocument.Uri);
            Workspace.PublishDiagnostics(projectDocument);

            // Only enable expression-related language service facilities if they're using our custom "MSBuild" language type (rather than "XML").
            projectDocument.EnableExpressions = parameters.TextDocument.LanguageId == "msbuild" && Workspace.Configuration.EnableExperimentalFeatures.Contains("expressions");

            Server.ClearBusy("Project loaded.");

            if (!projectDocument.HasXml)
            {
                Log.Warning("Failed to load project file {ProjectFilePath}.", projectDocument.ProjectFile.FullName);

                return;
            }

            switch (projectDocument)
            {
                case MasterProjectDocument masterProjectDocument:
                    {
                        Log.Information("Successfully loaded project {ProjectFilePath}.", projectDocument.ProjectFile.FullName);

                        break;
                    }
                case SubProjectDocument subProjectDocument:
                    {
                        Log.Information("Successfully loaded project {ProjectFilePath} as a sub-project of {MasterProjectFileName}.",
                            projectDocument.ProjectFile.FullName,
                            subProjectDocument.MasterProjectDocument.ProjectFile.Name
                        );

                        break;
                    }
            }

            if (Workspace.Configuration.LogLevelSwitch.MinimumLevel <= LogEventLevel.Debug)
            {
                Log.Debug("===========================");
                foreach (PackageSource packageSource in projectDocument.ConfiguredPackageSources)
                {
                    Log.Debug(" - Project uses package source {PackageSourceName} ({PackageSourceUrl})",
                        packageSource.Name,
                        packageSource.Source
                    );
                }

                Log.Debug("===========================");
                if (projectDocument.HasMSBuildProject)
                {
                    if (Workspace.Configuration.CompletionsFromProject.Contains(CompletionSource.Task))
                    {
                        Log.Debug("Scanning task definitions for project {ProjectName}...", projectDocument.ProjectFile.Name);
                        List<MSBuildTaskAssemblyMetadata> taskAssemblies = await projectDocument.GetMSBuildProjectTaskAssemblies();
                        Log.Debug("Scan complete for task definitions of project {ProjectName} ({AssemblyCount} assemblies scanned).", projectDocument.ProjectFile.Name, taskAssemblies.Count);

                        Log.Debug("===========================");
                    }

                    MSBuildObject[] msbuildObjects = projectDocument.MSBuildObjects.ToArray();
                    Log.Debug("MSBuild project loaded ({MSBuildObjectCount} MSBuild objects).", msbuildObjects.Length);

                    foreach (MSBuildObject msbuildObject in msbuildObjects)
                    {
                        Log.Debug("{Type:l}: {Kind} {Name} spanning {XmlRange}",
                            msbuildObject.GetType().Name,
                            msbuildObject.Kind,
                            msbuildObject.Name,
                            msbuildObject.XmlRange
                        );
                    }
                }
                else
                    Log.Debug("MSBuild project not loaded.");
            }
        }

        /// <summary>
        ///     Called when a text document is changed.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task OnDidChangeTextDocument(DidChangeTextDocumentParams parameters)
        {
            Log.Debug("Reloading project {ProjectFile}...",
                VSCodeDocumentUri.GetFileSystemPath(parameters.TextDocument.Uri)
            );

            TextDocumentContentChangeEvent mostRecentChange = parameters.ContentChanges.LastOrDefault();
            if (mostRecentChange == null)
                return;

            string updatedDocumentText = mostRecentChange.Text;
            ProjectDocument projectDocument = await Workspace.TryUpdateProjectDocument(parameters.TextDocument.Uri, updatedDocumentText);
            Workspace.PublishDiagnostics(projectDocument);

            if (Workspace.Configuration.LogLevelSwitch.MinimumLevel <= LogEventLevel.Debug)
            {
                if (projectDocument.HasMSBuildProject)
                {
                    if (!projectDocument.IsMSBuildProjectCached)
                    {
                        MSBuildObject[] msbuildObjects = projectDocument.MSBuildObjects.ToArray();
                        Log.Debug("MSBuild project reloaded ({MSBuildObjectCount} MSBuild objects).", msbuildObjects.Length);

                        foreach (MSBuildObject msbuildObject in msbuildObjects)
                        {
                            Log.Debug("{Type:l}: {Kind} {Name} spanning {XmlRange}",
                                msbuildObject.GetType().Name,
                                msbuildObject.Kind,
                                msbuildObject.Name,
                                msbuildObject.XmlRange
                            );
                        }
                    }
                    else
                        Log.Debug("MSBuild project not loaded; will used cached project state (as long as positional lookups are not required).");
                }
                else
                    Log.Debug("MSBuild project not loaded.");
            }
        }

        /// <summary>
        ///     Called when a text document is saved.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task OnDidSaveTextDocument(DidSaveTextDocumentParams parameters)
        {
            Log.Information("Reloading project {ProjectFile}...",
                VSCodeDocumentUri.GetFileSystemPath(parameters.TextDocument.Uri)
            );

            ProjectDocument projectDocument = await Workspace.GetProjectDocument(parameters.TextDocument.Uri, reload: true);
            Workspace.PublishDiagnostics(projectDocument);

            if (!projectDocument.HasXml)
            {
                Log.Warning("Failed to reload project file {ProjectFilePath} (XML is invalid).", projectDocument.ProjectFile.FullName);

                return;
            }

            if (!projectDocument.HasMSBuildProject)
            {
                Log.Warning("Reloaded project file {ProjectFilePath} (XML is valid, but MSBuild project is not).", projectDocument.ProjectFile.FullName);

                return;
            }

            Log.Information("Successfully reloaded project {ProjectFilePath}.", projectDocument.ProjectFile.FullName);
        }

        /// <summary>
        ///     Called when a text document is closed.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task OnDidCloseTextDocument(DidCloseTextDocumentParams parameters)
        {
            await Workspace.RemoveProjectDocument(parameters.TextDocument.Uri);

            Log.Information("Unloaded project {ProjectFile}.",
                VSCodeDocumentUri.GetFileSystemPath(parameters.TextDocument.Uri)
            );
        }

        /// <summary>
        ///     Get attributes for the specified text document.
        /// </summary>
        /// <param name="documentUri">
        ///     The document URI.
        /// </param>
        /// <returns>
        ///     The document attributes.
        /// </returns>
        TextDocumentAttributes GetTextDocumentAttributes(Uri documentUri)
        {
            string documentFilePath = VSCodeDocumentUri.GetFileSystemPath(documentUri);
            if (documentFilePath == null)
                return new TextDocumentAttributes(documentUri, "plaintext");

            string extension = Path.GetExtension(documentFilePath).ToLower();
            switch (extension)
            {
                case "props":
                case "targets":
                    {
                        break;
                    }
                default:
                    {
                        if (extension.EndsWith("proj"))
                            break;

                        return new TextDocumentAttributes(documentUri, "plaintext");
                    }
            }

            return new TextDocumentAttributes(documentUri, "msbuild");
        }

        /// <summary>
        ///     Handle a document being opened.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task INotificationHandler<DidOpenTextDocumentParams>.Handle(DidOpenTextDocumentParams parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (BeginOperation("OnDidOpenTextDocument"))
            {
                try
                {
                    await OnDidOpenTextDocument(parameters);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnDidOpenTextDocument");
                }
            }
        }

        /// <summary>
        ///     Handle a document being closed.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task INotificationHandler<DidCloseTextDocumentParams>.Handle(DidCloseTextDocumentParams parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (BeginOperation("OnDidCloseTextDocument"))
            {
                try
                {
                    await OnDidCloseTextDocument(parameters);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnDidCloseTextDocument");
                }
            }
        }

        /// <summary>
        ///     Handle a change in document text.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task INotificationHandler<DidChangeTextDocumentParams>.Handle(DidChangeTextDocumentParams parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (BeginOperation("OnDidChangeTextDocument"))
            {
                try
                {
                    await OnDidChangeTextDocument(parameters);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnDidChangeTextDocument");
                }
            }
        }

        /// <summary>
        ///     Handle a document being saved.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task INotificationHandler<DidSaveTextDocumentParams>.Handle(DidSaveTextDocumentParams parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (BeginOperation("OnDidSaveTextDocument"))
            {
                try
                {
                    await OnDidSaveTextDocument(parameters);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnDidSaveTextDocument");
                }
            }
        }

        /// <summary>
        ///     Get registration options for handling document events.
        /// </summary>
        /// <returns>
        ///     The registration options.
        /// </returns>
        TextDocumentRegistrationOptions IRegistration<TextDocumentRegistrationOptions>.GetRegistrationOptions() => DocumentRegistrationOptions;

        /// <summary>
        ///     Get registration options for handling document-change events.
        /// </summary>
        /// <returns>
        ///     The registration options.
        /// </returns>
        TextDocumentChangeRegistrationOptions IRegistration<TextDocumentChangeRegistrationOptions>.GetRegistrationOptions() => DocumentChangeRegistrationOptions;

        /// <summary>
        ///     Get registration options for handling document save events.
        /// </summary>
        /// <returns>
        ///     The registration options.
        /// </returns>
        TextDocumentSaveRegistrationOptions IRegistration<TextDocumentSaveRegistrationOptions>.GetRegistrationOptions() => DocumentSaveRegistrationOptions;

        /// <summary>
        ///     Called to inform the handler of the language server's document-synchronisation capabilities.
        /// </summary>
        /// <param name="capabilities">
        ///     A <see cref="SynchronizationCapability"/> data structure representing the capabilities.
        /// </param>
        void ICapability<SynchronizationCapability>.SetCapability(SynchronizationCapability capabilities)
        {
            SynchronizationCapabilities = capabilities;
        }

        /// <summary>
        ///     Get attributes for the specified text document.
        /// </summary>
        /// <param name="documentUri">
        ///     The document URI.
        /// </param>
        /// <returns>
        ///     The document attributes.
        /// </returns>
        TextDocumentAttributes ITextDocumentSyncHandler.GetTextDocumentAttributes(Uri documentUri)
        {
            if (documentUri == null)
                throw new ArgumentNullException(nameof(documentUri));

            return GetTextDocumentAttributes(documentUri);
        }
    }
}
