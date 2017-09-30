using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Abstractions;
using OmniSharp.Extensions.LanguageServer.Capabilities.Client;
using OmniSharp.Extensions.LanguageServer.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using NuGet.Versioning;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MSBuildProjectTools.LanguageServer.Handlers
{
    using CompletionProviders;
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Handler for completion requests.
    /// </summary>
    public sealed class CompletionHandler
        : Handler, ICompletionHandler
    {
        /// <summary>
        ///     Create a new <see cref="CompletionHandler"/>.
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
        public CompletionHandler(ILanguageServer server, Workspace workspace, ILogger logger)
            : base(server, logger)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            Workspace = workspace;
        }

        /// <summary>
        ///     Registered completion providers.
        /// </summary>
        public List<ICompletionProvider> Providers { get; } = new List<ICompletionProvider>();

        /// <summary>
        ///     The document workspace.
        /// </summary>
        Workspace Workspace { get; }

        /// <summary>
        ///     The language server configuration.
        /// </summary>
        Configuration Configuration { get; }

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
        ///     Get registration options for handling completion requests events.
        /// </summary>
        CompletionRegistrationOptions CompletionRegistrationOptions
        {
            get => new CompletionRegistrationOptions
            {
                DocumentSelector = DocumentSelector,
                TriggerCharacters = new string[] {
                    "<", // Element
                },
                ResolveProvider = false
            };
        }

        /// <summary>
        ///     Has the client supplied completion capabilities?
        /// </summary>
        bool HaveCompletionCapabilities => CompletionCapabilities != null;

        /// <summary>
        ///     The client's completion capabilities.
        /// </summary>
        CompletionCapability CompletionCapabilities { get; set; }

        /// <summary>
        ///     Should the handler return empty <see cref="CompletionList"/>s instead of <c>null</c>?
        /// </summary>
        bool ReturnEmptyCompletionLists => Workspace.Configuration.EnableExperimentalFeatures.Contains("empty-completion-lists");

        /// <summary>
        ///     A <see cref="CompletionList"/> (or <c>null</c>) representing no completions.
        /// </summary>
        CompletionList NoCompletions => ReturnEmptyCompletionLists ? new CompletionList(Enumerable.Empty<CompletionItem>(), isIncomplete: false) : null;

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
        async Task<CompletionList> OnCompletion(TextDocumentPositionParams parameters, CancellationToken cancellationToken)
        {
            ProjectDocument projectDocument = await Workspace.GetProjectDocument(parameters.TextDocument.Uri);

            XmlLocation location;

            bool isIncomplete = false;
            List<CompletionItem> completionItems = new List<CompletionItem>();
            using (await projectDocument.ReaderLockAsync(cancellationToken))
            {
                if (!projectDocument.HasXml)
                    return NoCompletions;

                Position position = parameters.Position.ToNative();
                Log.Debug("Completion requested for {Position:l}", position);

                location = projectDocument.XmlLocator.Inspect(position);
                if (location == null)
                {
                    Log.Debug("Completion short-circuited; nothing interesting at {Position:l}", position);

                    return NoCompletions;
                }

                Log.Debug("Completion will target {XmlLocation:l}", location);

                List<Task<CompletionList>> allProviderCompletions =
                    Providers.Select(async provider =>
                    {
                        using (LogContext.PushProperty("ProviderName", provider.Name))
                        using (Log.BeginTimedOperation("Invoke completion provider", level: LogEventLevel.Verbose))
                        {
                            return await provider.ProvideCompletions(location, projectDocument, cancellationToken);
                        }
                    })
                    .ToList();

                while (allProviderCompletions.Count > 0)
                {
                    Task<CompletionList> providerCompletionTask = await Task.WhenAny(allProviderCompletions);
                    allProviderCompletions.Remove(providerCompletionTask);

                    try
                    {
                        CompletionList providerCompletions = await providerCompletionTask;
                        if (providerCompletions != null)
                        {
                            completionItems.AddRange(providerCompletions.Items);

                            isIncomplete |= providerCompletions.IsIncomplete; // If any provider returns incomplete results, VSCode will need to ask again as the user continues to type.
                        }
                    }
                    catch (AggregateException aggregateSuggestionError)
                    {
                        foreach (Exception suggestionError in aggregateSuggestionError.Flatten().InnerExceptions)
                            Log.Error(suggestionError, "Failed to provide completions.");

                        return NoCompletions;
                    }
                    catch (Exception suggestionError)
                    {
                        Log.Error(suggestionError, "Failed to provide completions.");

                        return NoCompletions;
                    }
                }
            }

            Log.Debug("Offering a total of {CompletionCount} completions for {Location:l} (Exhaustive: {Exhaustive}).", completionItems.Count, location, !isIncomplete);

            if (completionItems.Count == 0 && !isIncomplete)
                return NoCompletions;

            CompletionList completionList = new CompletionList(completionItems, isIncomplete);

            return completionList;
        }

        /// <summary>
        ///     Handle a request for completion.
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
        async Task<CompletionList> IRequestHandler<TextDocumentPositionParams, CompletionList>.Handle(TextDocumentPositionParams parameters, CancellationToken cancellationToken)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (BeginOperation("OnCompletion"))
            {
                try
                {
                    return await OnCompletion(parameters, cancellationToken);
                }
                catch (Exception unexpectedError)
                {
                    Log.Error(unexpectedError, "Unhandled exception in {Method:l}.", "OnCompletion");

                    return null;
                }
            }
        }

        /// <summary>
        ///     Get registration options for handling completion requests.
        /// </summary>
        /// <returns>
        ///     The registration options.
        /// </returns>
        CompletionRegistrationOptions IRegistration<CompletionRegistrationOptions>.GetRegistrationOptions() => CompletionRegistrationOptions;

        /// <summary>
        ///     Called to inform the handler of the language server's completion capabilities.
        /// </summary>
        /// <param name="capabilities">
        ///     A <see cref="CompletionCapability"/> data structure representing the capabilities.
        /// </param>
        void ICapability<CompletionCapability>.SetCapability(CompletionCapability capabilities)
        {
            CompletionCapabilities = capabilities;
        }
    }
}
