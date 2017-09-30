using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Abstractions;
using OmniSharp.Extensions.LanguageServer.Capabilities.Client;
using OmniSharp.Extensions.LanguageServer.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Microsoft.Language.Xml;
using NuGet.Versioning;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
    
namespace MSBuildProjectTools.LanguageServer.Handlers
{
    using ContentProviders;
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Handler for document symbol requests.
    /// </summary>
    public sealed class DocumentSymbolHandler
        : Handler, IDocumentSymbolHandler
    {
        /// <summary>
        ///     Create a new <see cref="DocumentSymbolHandler"/>.
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
        public DocumentSymbolHandler(ILanguageServer server, Workspace workspace, ILogger logger)
            : base(server, logger)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            Workspace = workspace;
        }

        /// <summary>
        ///     The document workspace.
        /// </summary>
        Workspace Workspace { get; }

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
        ///     Has the client supplied document symbol capabilities?
        /// </summary>
        bool HaveDocumentSymbolCapabilities => DocumentSymbolCapabilities != null;

        /// <summary>
        ///     The client's document symbol capabilities.
        /// </summary>
        DocumentSymbolCapability DocumentSymbolCapabilities { get; set; }

        /// <summary>
        ///     Called when completions are requested.
        /// </summary>
        /// <param name="parameters">
        ///     The request parameters.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the request.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation whose result is the completion list or <c>null</c> if no completions are provided.
        /// </returns>
        async Task<SymbolInformationContainer> OnDocumentSymbols(DocumentSymbolParams parameters, CancellationToken cancellationToken)
        {
            ProjectDocument projectDocument = await Workspace.GetProjectDocument(parameters.TextDocument.Uri);

            List<SymbolInformation> symbols = new List<SymbolInformation>();
            using (await projectDocument.ReaderLockAsync(cancellationToken))
            {
                // We need a valid MSBuild project with up-to-date positional information.
                if (!projectDocument.HasMSBuildProject || projectDocument.IsMSBuildProjectCached)
                    return null;

                foreach (MSBuildObject msbuildObject in projectDocument.MSBuildObjects)
                {
                    // Special case for item groups, which can contribute multiple symbols from a single item group.
                    if (msbuildObject is MSBuildItemGroup itemGroup)
                    {
                        symbols.AddRange(itemGroup.Includes.Select(include =>
                        {
                            string trimmedInclude = String.Join(";",
                                include.Split(
                                    new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries
                                )
                                .Select(includedItem => includedItem.Trim())
                            );
                                

                            return new SymbolInformation
                            {
                                Name = $"{itemGroup.Name} ({trimmedInclude})",
                                Kind = SymbolKind.Array,
                                ContainerName = "Item",
                                Location = new Location
                                {
                                    Uri = projectDocument.DocumentUri,
                                    Range = msbuildObject.XmlRange.ToLsp()
                                }
                            };
                        }));

                        continue;
                    }

                    SymbolInformation symbol = new SymbolInformation
                    {
                        Name = msbuildObject.Name,
                        Location = new Location
                        {
                            Uri = projectDocument.DocumentUri,
                            Range = msbuildObject.XmlRange.ToLsp()
                        }
                    };
                    if (msbuildObject is MSBuildTarget)
                    {
                        symbol.ContainerName = "Target";
                        symbol.Kind = SymbolKind.Function;
                    }
                    else if (msbuildObject is MSBuildProperty)
                    {
                        symbol.ContainerName = "Property";
                        symbol.Kind = SymbolKind.Property;
                    }
                    else if (msbuildObject is MSBuildImport)
                    {
                        symbol.ContainerName = "Import";
                        symbol.Kind = SymbolKind.Package;
                    }
                    else if (msbuildObject is MSBuildSdkImport)
                    {
                        symbol.ContainerName = "Import (SDK)";
                        symbol.Kind = SymbolKind.Package;
                    }
                    else
                        continue;

                    symbols.Add(symbol);
                }
            }

            if (symbols.Count == 0)
                return null;

            return new SymbolInformationContainer(
                symbols.OrderBy(symbol => symbol.Name)
            );
        }

        /// <summary>
        ///     Get registration options for handling document events.
        /// </summary>
        /// <returns>
        ///     The registration options.
        /// </returns>
        TextDocumentRegistrationOptions IRegistration<TextDocumentRegistrationOptions>.GetRegistrationOptions() => DocumentRegistrationOptions;

        /// <summary>
        ///     Handle a request for document symbols.
        /// </summary>
        /// <param name="parameters">
        ///     The request parameters.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the request.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation whose result is the symbol container or <c>null</c> if no symbols are provided.
        /// </returns>
        async Task<SymbolInformationContainer> IRequestHandler<DocumentSymbolParams, SymbolInformationContainer>.Handle(DocumentSymbolParams parameters, CancellationToken cancellationToken)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            
            using (BeginOperation("OnDocumentSymbols"))
            {
                try
                {
                    return await OnDocumentSymbols(parameters, cancellationToken);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnDocumentSymbols");

                    return null;
                }
            }
        }

        /// <summary>
        ///     Called to inform the handler of the language server's document symbol capabilities.
        /// </summary>
        /// <param name="capabilities">
        ///     A <see cref="DocumentSymbolCapability"/> data structure representing the capabilities.
        /// </param>
        void ICapability<DocumentSymbolCapability>.SetCapability(DocumentSymbolCapability capabilities)
        {
            DocumentSymbolCapabilities = capabilities;
        }
    }
}
