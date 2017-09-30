using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Abstractions;
using OmniSharp.Extensions.LanguageServer.Capabilities.Client;
using OmniSharp.Extensions.LanguageServer.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Microsoft.Language.Xml;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MSBuildProjectTools.LanguageServer.Handlers
{
    using ContentProviders;
    using Documents;
    using SemanticModel;
    using SemanticModel.MSBuildExpressions;
    using Utilities;

    /// <summary>
    ///     Handler for document hover requests.
    /// </summary>
    public sealed class HoverHandler
        : Handler, IHoverHandler
    {
        /// <summary>
        ///     Create a new <see cref="HoverHandler"/>.
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
        public HoverHandler(ILanguageServer server, Workspace workspace, ILogger logger)
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
        ///     Has the client supplied hover capabilities?
        /// </summary>
        bool HaveHoverCapabilities => HoverCapabilities != null;

        /// <summary>
        ///     The client's hover capabilities.
        /// </summary>
        HoverCapability HoverCapabilities { get; set; }

        /// <summary>
        ///     Called when the mouse pointer hovers over text.
        /// </summary>
        /// <param name="parameters">
        ///     The notification parameters.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A <see cref="Task{TResult}"/> whose result is the hover details, or <c>null</c> if no hover details are provided by the handler.
        /// </returns>
        async Task<Hover> OnHover(TextDocumentPositionParams parameters, CancellationToken cancellationToken)
        {
            if (Workspace.Configuration.DisableHover)
                return null;

            ProjectDocument projectDocument = await Workspace.GetProjectDocument(parameters.TextDocument.Uri);

            using (await projectDocument.ReaderLockAsync(cancellationToken))
            {
                // This won't work if we can't inspect the MSBuild project state and match it up to the target position.
                if (!projectDocument.HasMSBuildProject || projectDocument.IsMSBuildProjectCached)
                    return null;

                Position position = parameters.Position.ToNative();

                XmlLocation location = projectDocument.XmlLocator.Inspect(position);
                if (location == null)
                    return null;

                if (!location.IsElementOrAttribute())
                    return null;

                // Match up the MSBuild item / property with its corresponding XML element / attribute.
                MSBuildObject msbuildObject;

                MarkedStringContainer hoverContent = null;
                HoverContentProvider contentProvider = new HoverContentProvider(projectDocument);
                if (location.IsElement(out XSElement element))
                {
                    msbuildObject = projectDocument.GetMSBuildObjectAtPosition(element.Start);
                    switch (msbuildObject)
                    {
                        case MSBuildProperty property:
                        {
                            hoverContent = contentProvider.Property(property);

                            break;
                        }
                        case MSBuildUnusedProperty unusedProperty:
                        {
                            hoverContent = contentProvider.UnusedProperty(unusedProperty);

                            break;
                        }
                        case MSBuildItemGroup itemGroup:
                        {
                            hoverContent = contentProvider.ItemGroup(itemGroup);

                            break;
                        }
                        case MSBuildUnusedItemGroup unusedItemGroup:
                        {
                            hoverContent = contentProvider.UnusedItemGroup(unusedItemGroup);

                            break;
                        }
                        case MSBuildTarget target:
                        {
                            hoverContent = contentProvider.Target(target);

                            break;
                        }
                        case MSBuildImport import:
                        {
                            hoverContent = contentProvider.Import(import);

                            break;
                        }
                        case MSBuildUnresolvedImport unresolvedImport:
                        {
                            hoverContent = contentProvider.UnresolvedImport(unresolvedImport);

                            break;
                        }
                    }
                }
                else if (location.IsElementText(out XSElementText text))
                {
                    msbuildObject = projectDocument.GetMSBuildObjectAtPosition(text.Element.Start);
                    switch (msbuildObject)
                    {
                        case MSBuildProperty property:
                        {
                            hoverContent = contentProvider.Property(property);

                            break;
                        }
                        case MSBuildUnusedProperty unusedProperty:
                        {
                            hoverContent = contentProvider.UnusedProperty(unusedProperty);

                            break;
                        }
                    }
                }
                else if (location.IsAttribute(out XSAttribute attribute))
                {
                    msbuildObject = projectDocument.GetMSBuildObjectAtPosition(attribute.Element.Start);
                    switch (msbuildObject)
                    {
                        case MSBuildItemGroup itemGroup:
                        {
                            hoverContent = contentProvider.ItemGroupMetadata(itemGroup, attribute.Name);

                            break;
                        }
                        case MSBuildUnusedItemGroup unusedItemGroup:
                        {
                            hoverContent = contentProvider.UnusedItemGroupMetadata(unusedItemGroup, attribute.Name);

                            break;
                        }
                        case MSBuildSdkImport sdkImport:
                        {
                            hoverContent = contentProvider.SdkImport(sdkImport);

                            break;
                        }
                        case MSBuildUnresolvedSdkImport unresolvedSdkImport:
                        {
                            hoverContent = contentProvider.UnresolvedSdkImport(unresolvedSdkImport);

                            break;
                        }
                        case MSBuildImport import:
                        {
                            hoverContent = contentProvider.Import(import);

                            break;
                        }
                        default:
                        {
                            if (attribute.Name == "Condition")
                                hoverContent = contentProvider.Condition(attribute.Element.Name, attribute.Value);

                            break;
                        }
                    }
                }

                if (hoverContent == null)
                    return null;

                return new Hover
                {
                    Contents = hoverContent,
                    Range = location.Node.Range.ToLsp()
                };
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
        ///     Handle a request for hover information.
        /// </summary>
        /// <param name="parameters">
        ///     The request parameters.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the request.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation whose result is the hover details or <c>null</c> if no hover details are provided.
        /// </returns>
        async Task<Hover> IRequestHandler<TextDocumentPositionParams, Hover>.Handle(TextDocumentPositionParams parameters, CancellationToken cancellationToken)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            try
            {
                return await OnHover(parameters, cancellationToken);
            }
            catch (Exception unexpectedError)
            {
                Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnHover");

                return null;
            }
        }

        /// <summary>
        ///     Called to inform the handler of the language server's hover capabilities.
        /// </summary>
        /// <param name="capabilities">
        ///     A <see cref="HoverCapability"/> data structure representing the capabilities.
        /// </param>
        void ICapability<HoverCapability>.SetCapability(HoverCapability capabilities)
        {
            HoverCapabilities = capabilities;
        }
    }
}
