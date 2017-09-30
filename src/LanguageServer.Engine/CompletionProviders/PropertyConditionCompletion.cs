using OmniSharp.Extensions.LanguageServer.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    ///     Completion provider the Condition attribute of a property element.
    /// </summary>
    public class PropertyConditionCompletion
        : CompletionProvider
    {
        /// <summary>
        ///     Create a new <see cref="PropertyConditionCompletion"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public PropertyConditionCompletion(ILogger logger)
            : base(logger)
        {
        }

        /// <summary>
        ///     The provider display name.
        /// </summary>
        public override string Name => "Top-level Elements";

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

            List<CompletionItem> completions = new List<CompletionItem>();

            using (await projectDocument.ReaderLockAsync())
            {
                XSAttribute conditionAttribute;
                if (!location.IsAttributeValue(out conditionAttribute) || conditionAttribute.Name != "Condition")
                    return null;

                if (conditionAttribute.Element.ParentElement?.Name != "PropertyGroup")
                    return null;

                LspModels.Range replaceRange = conditionAttribute.ValueRange.ToLsp();
                
                completions.Add(new CompletionItem
                {
                    Label = "If not already defined",
                    Detail = "Condition",
                    Documentation = "Only use this property if the property does not already have a value.",
                    TextEdit = new TextEdit
                    {
                        NewText = $"'$({conditionAttribute.Element.Name})' == ''",
                        Range = replaceRange
                    }
                });
            }

            if (completions.Count == 0)
                return null;

            return new CompletionList(completions, isIncomplete: false);
        }
    }
}
