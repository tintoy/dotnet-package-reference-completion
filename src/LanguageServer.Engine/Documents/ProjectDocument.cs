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
    using Microsoft.Build.Execution;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Represents the document state for an MSBuild project.
    /// </summary>
    public abstract class ProjectDocument
    {
        /// <summary>
        ///     Diagnostics (if any) for the project.
        /// </summary>
        readonly List<Lsp.Models.Diagnostic> _diagnostics = new List<Lsp.Models.Diagnostic>();

        /// <summary>
        ///     The project's configured package sources.
        /// </summary>
        readonly List<PackageSource> _configuredPackageSources = new List<PackageSource>();
        
        /// <summary>
        ///     NuGet auto-complete APIs for configured package sources.
        /// </summary>
        readonly List<AutoCompleteResource> _autoCompleteResources = new List<AutoCompleteResource>();

        /// <summary>
        ///     The underlying MSBuild project collection.
        /// </summary>
        public ProjectCollection MSBuildProjectCollection { get; protected set; }

        /// <summary>
        ///     The underlying MSBuild project.
        /// </summary>
        public Project MSBuildProject { get; protected set; }

        /// <summary>
        ///     Is the underlying MSBuild project cached (i.e. out-of-date with respect to the source text)?
        /// </summary>
        /// <remarks>
        ///     If the current project XML is invalid, the original MSBuild project is retained, but <see cref="MSBuildLocator"/> functionality will be unavailable (since source positions may no longer match up).
        /// </remarks>
        public bool IsMSBuildProjectCached { get; private set; }

        /// <summary>
        ///     Create a new <see cref="ProjectDocument"/>.
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
        protected ProjectDocument(Workspace workspace, Uri documentUri, ILogger logger)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            if (documentUri == null)
                throw new ArgumentNullException(nameof(documentUri));

            Workspace = workspace;
            DocumentUri = documentUri;
            ProjectFile = new FileInfo(
                VSCodeDocumentUri.GetFileSystemPath(documentUri)
            );

            if (ProjectFile.Extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                Kind = ProjectDocumentKind.Project;
            else if (ProjectFile.Extension.Equals(".props", StringComparison.OrdinalIgnoreCase))
                Kind = ProjectDocumentKind.Properties;
            else if (ProjectFile.Extension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
                Kind = ProjectDocumentKind.Targets;
            else
                throw new ArgumentException($"Unexpected project file extension '{ProjectFile.Extension}'.", nameof(documentUri));

            Log = logger.ForContext("ProjectDocument", ProjectFile.FullName);
        }

        /// <summary>
        ///     The document workspace.
        /// </summary>
        public Workspace Workspace { get; }

        /// <summary>
        ///     The project document URI.
        /// </summary>
        public Uri DocumentUri { get; }

        /// <summary>
        ///     The project file.
        /// </summary>
        public FileInfo ProjectFile { get; }

        /// <summary>
        ///     The kind of project document.
        /// </summary>
        public ProjectDocumentKind Kind { get; }

        /// <summary>
        ///     A lock used to control access to project state.
        /// </summary>
        public AsyncReaderWriterLock Lock { get; } = new AsyncReaderWriterLock();

        /// <summary>
        ///     Are there currently any diagnostics to be published for the project?
        /// </summary>
        public bool HasDiagnostics => _diagnostics.Count > 0;

        /// <summary>
        ///     Diagnostics (if any) for the project.
        /// </summary>
        public IReadOnlyList<Lsp.Models.Diagnostic> Diagnostics => _diagnostics;

        /// <summary>
        ///     The parsed project XML.
        /// </summary>
        public XmlDocumentSyntax Xml { get; protected set; }

        /// <summary>
        ///     Is the project XML currently loaded?
        /// </summary>
        public bool HasXml => Xml != null && XmlPositions != null;

        /// <summary>
        ///     Is the underlying MSBuild project currently loaded?
        /// </summary>
        public bool HasMSBuildProject => HasXml && MSBuildProjectCollection != null && MSBuildProject != null;

        /// <summary>
        ///     Does the project have in-memory changes?
        /// </summary>
        public bool IsDirty { get; protected set; }

        /// <summary>
        ///     The textual position translator for the project XML .
        /// </summary>
        public TextPositions XmlPositions { get; protected set; }

        /// <summary>
        ///     The project XML node lookup facility.
        /// </summary>
        public XmlLocator XmlLocator { get; protected set; }

        /// <summary>
        ///     The project MSBuild object-lookup facility.
        /// </summary>
        protected MSBuildLocator MSBuildLocator { get; private set; }

        /// <summary>
        ///     MSBuild objects in the project that correspond to locations in the file.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     The project is cached or not loaded.
        /// </exception>
        public IEnumerable<MSBuildObject> MSBuildObjects
        {
            get
            {
                if (!HasMSBuildProject)
                    throw new InvalidOperationException($"MSBuild project '{ProjectFile.FullName}' is not loaded.");

                if (IsMSBuildProjectCached)
                    throw new InvalidOperationException($"MSBuild project '{ProjectFile.FullName}' is a cached (out-of-date) copy because the project XML is currently invalid; positional lookups can't work in this scenario.");

                return MSBuildLocator.AllObjects;
            }
        }

        /// <summary>
        ///     NuGet package sources configured for the current project.
        /// </summary>
        public IReadOnlyList<PackageSource> ConfiguredPackageSources => _configuredPackageSources;

        /// <summary>
        ///     The document's logger.
        /// </summary>
        protected ILogger Log { get; set; }

        /// <summary>
        ///     Inspect the specified location in the XML.
        /// </summary>
        /// <param name="position">
        ///     The location's position.
        /// </param>
        /// <returns>
        ///     An <see cref="XmlLocation"/> representing the result of the inspection.
        /// </returns>
        public XmlLocation InspectXml(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            if (!HasXml)
                throw new InvalidOperationException($"XML for project '{ProjectFile.FullName}' is not loaded.");

            return XmlLocator.Inspect(position);
        }

        /// <summary>
        ///     Load and parse the project.
        /// </summary>
        /// <param name="cancellationToken">
        ///     An optional <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A task representing the load operation.
        /// </returns>
        public virtual async Task Load(CancellationToken cancellationToken = default(CancellationToken))
        {
            ClearDiagnostics();

            Xml = null;
            XmlPositions = null;
            XmlLocator = null;

            string xml;
            using (StreamReader reader = ProjectFile.OpenText())
            {
                xml = await reader.ReadToEndAsync();
            }
            Xml = Parser.ParseText(xml);
            XmlPositions = new TextPositions(xml);
            XmlLocator = new XmlLocator(Xml, XmlPositions);

            IsDirty = false;

            await ConfigurePackageSources(cancellationToken);

            bool loaded = TryLoadMSBuildProject();
            if (loaded)
                MSBuildLocator = new MSBuildLocator(MSBuildProject, Xml, XmlPositions);
            else
                MSBuildLocator = null;

            IsMSBuildProjectCached = !loaded;
        }

        /// <summary>
        ///     Update the project in-memory state.
        /// </summary>
        /// <param name="xml">
        ///     The project XML.
        /// </param>
        public virtual void Update(string xml)
        {
            if (xml == null)
                throw new ArgumentNullException(nameof(xml));

            ClearDiagnostics();

            Xml = Parser.ParseText(xml);
            XmlPositions = new TextPositions(xml);
            XmlLocator = new XmlLocator(Xml, XmlPositions);
            IsDirty = true;

            bool loaded = TryLoadMSBuildProject();
            if (loaded)
                MSBuildLocator = new MSBuildLocator(MSBuildProject, Xml, XmlPositions);
            else
                MSBuildLocator = null;

            IsMSBuildProjectCached = !loaded;
        }

        /// <summary>
        ///     Determine the NuGet package sources configured for the current project and create clients for them.
        /// </summary>
        /// <param name="cancellationToken">
        ///     An optional <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the package sources were loaded; otherwise, <c>false</c>.
        /// </returns>
        public virtual async Task<bool> ConfigurePackageSources(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                _configuredPackageSources.Clear();
                _autoCompleteResources.Clear();

                _configuredPackageSources.AddRange(
                    NuGetHelper.GetWorkspacePackageSources(ProjectFile.Directory.FullName)
                        .Where(packageSource => packageSource.IsHttp)
                );
                _autoCompleteResources.AddRange(
                    await NuGetHelper.GetAutoCompleteResources(_configuredPackageSources, cancellationToken)
                );

                return true;
            }
            catch (Exception packageSourceLoadError)
            {
                Log.Error(packageSourceLoadError, "Error configuring NuGet package sources for MSBuild project '{ProjectFileName}'.", ProjectFile.FullName);

                return false;
            }
        }

        /// <summary>
        ///     Suggest package Ids based on the specified package Id prefix.
        /// </summary>
        /// <param name="packageIdPrefix">
        ///     The package Id prefix.
        /// </param>
        /// <param name="cancellationToken">
        ///     An optional <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A task that resolves to a sorted set of suggested package Ids.
        /// </returns>
        public virtual async Task<SortedSet<string>> SuggestPackageIds(string packageIdPrefix, CancellationToken cancellationToken = default(CancellationToken))
        {
            // We don't actually need a working MSBuild project for this.
            if (!HasXml)
                throw new InvalidOperationException($"XML for project '{ProjectFile.FullName}' is not loaded.");

            SortedSet<string> packageIds = await _autoCompleteResources.SuggestPackageIds(packageIdPrefix, includePrerelease: true, cancellationToken: cancellationToken);
            
            return packageIds;
        }

        /// <summary>
        ///     Suggest package versions for the specified package.
        /// </summary>
        /// <param name="packageId">
        ///     The package Id.
        /// </param>
        /// <param name="cancellationToken">
        ///     An optional <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A task that resolves to a sorted set of suggested package versions.
        /// </returns>
        public virtual async Task<SortedSet<NuGetVersion>> SuggestPackageVersions(string packageId, CancellationToken cancellationToken = default(CancellationToken))
        {
            // We don't actually need a working MSBuild project for this.
            if (!HasXml)
                throw new InvalidOperationException($"XML for project '{ProjectFile.FullName}' is not loaded.");

            SortedSet<NuGetVersion> packageVersions = await _autoCompleteResources.SuggestPackageVersions(packageId, includePrerelease: true, cancellationToken: cancellationToken);

            return packageVersions;
        }

        /// <summary>
        ///     Unload the project.
        /// </summary>
        public virtual void Unload()
        {
            TryUnloadMSBuildProject();
            MSBuildLocator = null;
            IsMSBuildProjectCached = false;

            Xml = null;
            XmlPositions = null;
            IsDirty = false;
        }

        /// <summary>
        ///     Get the XML object (if any) at the specified position in the project file.
        /// </summary>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     The object, or <c>null</c> if no object was found at the specified position.
        /// </returns>
        public SyntaxNode GetXmlAtPosition(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            if (!HasXml)
                throw new InvalidOperationException($"XML for project '{ProjectFile.FullName}' is not loaded.");

            int absolutePosition = XmlPositions.GetAbsolutePosition(position);

            return Xml.FindNode(position, XmlPositions);
        }

        /// <summary>
        ///     Get the XML object (if any) at the specified position in the project file.
        /// </summary>
        /// <typeparam name="TXml">
        ///     The type of XML object to return.
        /// </typeparam>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     The object, or <c>null</c> no object of the specified type was found at the specified position.
        /// </returns>
        public TXml GetXmlAtPosition<TXml>(Position position)
            where TXml : SyntaxNode
        {
            return GetXmlAtPosition(position) as TXml;
        }

        /// <summary>
        ///     Get the MSBuild object (if any) at the specified position in the project file.
        /// </summary>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     The MSBuild object, or <c>null</c> no object was found at the specified position.
        /// </returns>
        public MSBuildObject GetMSBuildObjectAtPosition(Position position)
        {
            if (!HasMSBuildProject)
                throw new InvalidOperationException($"MSBuild project '{ProjectFile.FullName}' is not loaded.");

            if (IsMSBuildProjectCached)
                throw new InvalidOperationException($"MSBuild project '{ProjectFile.FullName}' is a cached (out-of-date) copy because the project XML is currently invalid; positional lookups can't work in this scenario.");

            return MSBuildLocator.Find(position);
        }

        /// <summary>
        ///     Attempt to load the underlying MSBuild project.
        /// </summary>
        /// <returns>
        ///     <c>true</c>, if the project was successfully loaded; otherwise, <c>false</c>.
        /// </returns>
        protected abstract bool TryLoadMSBuildProject();

        /// <summary>
        ///     Attempt to unload the underlying MSBuild project.
        /// </summary>
        /// <returns>
        ///     <c>true</c>, if the project was successfully unloaded; otherwise, <c>false</c>.
        /// </returns>
        protected abstract bool TryUnloadMSBuildProject();

        /// <summary>
        ///     Remove all diagnostics for the project file.
        /// </summary>
        protected void ClearDiagnostics()
        {
            _diagnostics.Clear();
        }

        /// <summary>
        ///     Add a diagnostic to be published for the project file.
        /// </summary>
        /// <param name="severity">
        ///     The diagnostic severity.
        /// </param>
        /// <param name="message">
        ///     The diagnostic message.
        /// </param>
        /// <param name="range">
        ///     The range of text within the project XML that the diagnostic relates to.
        /// </param>
        /// <param name="diagnosticCode">
        ///     A code to identify the diagnostic type.
        /// </param>
        protected void AddDiagnostic(Lsp.Models.DiagnosticSeverity severity, string message, Range range, string diagnosticCode)
        {
            if (String.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'message'.", nameof(message));
            
            _diagnostics.Add(new Lsp.Models.Diagnostic
            {
                Severity = severity,
                Code = new Lsp.Models.DiagnosticCode(diagnosticCode),
                Message = message,
                Range = range.ToLsp(),
                Source = ProjectFile.FullName
            });
        }

        /// <summary>
        ///     Add an error diagnostic to be published for the project file.
        /// </summary>
        /// <param name="message">
        ///     The diagnostic message.
        /// </param>
        /// <param name="range">
        ///     The range of text within the project XML that the diagnostic relates to.
        /// </param>
        /// <param name="diagnosticCode">
        ///     A code to identify the diagnostic type.
        /// </param>
        protected void AddErrorDiagnostic(string message, Range range, string diagnosticCode) => AddDiagnostic(Lsp.Models.DiagnosticSeverity.Error, message, range, diagnosticCode);

        /// <summary>
        ///     Add a warning diagnostic to be published for the project file.
        /// </summary>
        /// <param name="message">
        ///     The diagnostic message.
        /// </param>
        /// <param name="range">
        ///     The range of text within the project XML that the diagnostic relates to.
        /// </param>
        /// <param name="diagnosticCode">
        ///     A code to identify the diagnostic type.
        /// </param>
        protected void AddWarningDiagnostic(string message, Range range, string diagnosticCode) => AddDiagnostic(Lsp.Models.DiagnosticSeverity.Warning, message, range, diagnosticCode);

        /// <summary>
        ///     Add an informational diagnostic to be published for the project file.
        /// </summary>
        /// <param name="message">
        ///     The diagnostic message.
        /// </param>
        /// <param name="range">
        ///     The range of text within the project XML that the diagnostic relates to.
        /// </param>
        /// <param name="diagnosticCode">
        ///     A code to identify the diagnostic type.
        /// </param>
        protected void AddInformationDiagnostic(string message, Range range, string diagnosticCode) => AddDiagnostic(Lsp.Models.DiagnosticSeverity.Information, message, range, diagnosticCode);

        /// <summary>
        ///     Add a hint diagnostic to be published for the project file.
        /// </summary>
        /// <param name="message">
        ///     The diagnostic message.
        /// </param>
        /// <param name="range">
        ///     The range of text within the project XML that the diagnostic relates to.
        /// </param>
        /// <param name="diagnosticCode">
        ///     A code to identify the diagnostic type.
        /// </param>
        protected void AddHintDiagnostic(string message, Range range, string diagnosticCode) => AddDiagnostic(Lsp.Models.DiagnosticSeverity.Hint, message, range, diagnosticCode);
    }
}
