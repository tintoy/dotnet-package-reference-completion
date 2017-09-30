using OmniSharp.Extensions.LanguageServer.Models;
using NuGet.Versioning;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using LspModels = OmniSharp.Extensions.LanguageServer.Models;

namespace MSBuildProjectTools.LanguageServer.CompletionProviders
{
    using Documents;
    using SemanticModel;
    using Utilities;

    /// <summary>
    ///     Completion provider for "PackageReference" and "DotNetCliToolReference" items.
    /// </summary>
    public class PackageReferenceCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="PackageReferenceCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public PackageReferenceCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Package Reference Items";

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

            bool isIncomplete = false;
            List<CompletionItem> completions = new List<CompletionItem>();

            Log.Debug("Evaluate completions for {XmlLocation:l}", location);

            using (await projectDocument.Lock.ReaderLockAsync())
            {
                if (location.CanCompleteAttributeValue(out XSAttribute attribute, "PackageReference", "Include", "Version"))
                {
                    Log.Debug("Offering completions for value of attribute {AttributeName} of element {ElementName} @ {Position:l}",
                        attribute.Name,
                        attribute.Element.Name,
                        location.Position
                    );

                    List<CompletionItem> packageCompletions = await HandlePackageReferenceAttributeCompletion(projectDocument, attribute, cancellationToken);
                    if (packageCompletions != null)
                    {
                        isIncomplete |= packageCompletions.Count > 10; // Default page size.
                        completions.AddRange(packageCompletions);
                    }
                }
                else if (location.CanCompleteElement(out XSElement replaceElement, asChildOfElementNamed: "ItemGroup"))
                {
                    if (replaceElement != null)
                    {
                        Log.Debug("Offering completions to replace child element @ {ReplaceRange} of {ElementName} @ {Position:l}",
                            replaceElement.Range,
                            "ItemGroup",
                            location.Position
                        );
                    }
                    else
                    {
                        Log.Debug("Offering completions for new child element of {ElementName} @ {Position:l}",
                            "ItemGroup",
                            location.Position
                        );
                    }

                    List<CompletionItem> elementCompletions = HandlePackageReferenceElementCompletion(location, projectDocument, replaceElement);
                    if (elementCompletions != null)
                        completions.AddRange(elementCompletions);
                }
                else
                    Log.Debug("Not offering any completions for {XmlLocation:l}", location);
            }

            Log.Debug("Offering {CompletionCount} completions for {XmlLocation:l}", completions.Count, location);

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions, isIncomplete);
        }

        /// <summary>
        ///     Handle completion for an attribute of a PackageReference element.
        /// </summary>
        /// <param name="projectDocument">
        ///     The current project document.
        /// </param>
        /// <param name="attribute">
        ///     The attribute for which completion is being requested.
        /// </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken"/> that can be used to cancel the operation.
        /// </param>
        /// <returns>
        ///     A <see cref="Task"/> representing the operation.
        /// </returns>
        async Task<List<CompletionItem>> HandlePackageReferenceAttributeCompletion(ProjectDocument projectDocument, XSAttribute attribute, CancellationToken cancellationToken)
        {
            if (attribute.Name == "Include")
            {
                string packageIdPrefix = attribute.Value;
                SortedSet<string> packageIds = await projectDocument.SuggestPackageIds(packageIdPrefix, cancellationToken);

                var completionItems = new List<CompletionItem>(
                    packageIds.Select(packageId => new CompletionItem
                    {
                        Label = packageId,
                        Detail = "Package Id",
                        Kind = CompletionItemKind.Module,
                        TextEdit = new TextEdit
                        {
                            Range = attribute.ValueRange.ToLsp(),
                            NewText = packageId
                        }
                    })
                );

                return completionItems;
            }

            if (attribute.Name == "Version")
            {
                XSAttribute includeAttribute = attribute.Element["Include"];
                if (includeAttribute == null)
                    return null;

                string packageId = includeAttribute.Value;
                IEnumerable<NuGetVersion> packageVersions = await projectDocument.SuggestPackageVersions(packageId, cancellationToken);
                if (projectDocument.Workspace.Configuration.NuGet.ShowNewestVersionsFirst)
                    packageVersions = packageVersions.Reverse();

                LspModels.Range replacementRange = attribute.ValueRange.ToLsp();

                var completionItems = new List<CompletionItem>(
                    packageVersions.Select((packageVersion, index) => new CompletionItem
                    {
                        Label = packageVersion.ToNormalizedString(),
                        Detail = "Package Version",
                        SortText = projectDocument.Workspace.Configuration.NuGet.ShowNewestVersionsFirst ? $"NuGet{index:00}" : null, // Override default sort order if configured to do so.
                        Kind = CompletionItemKind.Field,
                        TextEdit = new TextEdit
                        {
                            Range = replacementRange,
                            NewText = packageVersion.ToNormalizedString()
                        }
                    })
                );

                return completionItems;
            }

            // No completions.
            return null;
        }

        /// <summary>
        ///     Handle completion for an attribute of a PackageReference element.
        /// </summary>
        /// <param name="location">
        ///     The location where completion will be offered.
        /// </param>
        /// <param name="projectDocument">
        ///     The current project document.
        /// </param>
        /// <param name="replaceElement">
        ///     The element (if any) that will be replaced by the completion.
        /// </param>
        /// <returns>
        ///     The completion list or <c>null</c> if no completions are provided.
        /// </returns>
        List<CompletionItem> HandlePackageReferenceElementCompletion(XmlLocation location, ProjectDocument projectDocument, XSElement replaceElement)
        {
            if (projectDocument == null)
                throw new ArgumentNullException(nameof(projectDocument));

            if (location == null)
                throw new ArgumentNullException(nameof(location));

            Range replaceRange = replaceElement?.Range ?? location.Position.ToEmptyRange();

            return new List<CompletionItem>
            {
                new CompletionItem
                {
                    Label = "<PackageReference />",
                    Detail = "Element",
                    Documentation = "A NuGet package",
                    SortText = "1000<PackageReference />",
                    Kind = CompletionItemKind.File,
                    TextEdit = new TextEdit
                    {
                        NewText = "<PackageReference Include=\"${1:PackageId}\" Version=\"${2:PackageVersion}\" />$0",
                        Range = replaceRange.ToLsp()
                    },
                    InsertTextFormat = InsertTextFormat.Snippet
                },
                new CompletionItem
                {
                    Label = "<DotNetCliToolReference />",
                    Detail = "Element",
                    Documentation = "A command extension package for the dotnet CLI",
                    Kind = CompletionItemKind.File,
                    SortText = "1000<DotNetCliToolReference />",
                    TextEdit = new TextEdit
                    {
                        NewText = "<DotNetCliToolReference Include=\"${1:PackageId}\" Version=\"${2:PackageVersion}\" />$0",
                        Range = replaceRange.ToLsp()
                    },
                    InsertTextFormat = InsertTextFormat.Snippet
                }
            };
        }
    }
}
