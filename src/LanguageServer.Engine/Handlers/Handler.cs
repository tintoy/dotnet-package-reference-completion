using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Capabilities.Client;
using OmniSharp.Extensions.LanguageServer.Capabilities.Server;
using OmniSharp.Extensions.LanguageServer.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.IO;
using System.Reactive.Disposables;

namespace MSBuildProjectTools.LanguageServer.Handlers
{
    using Serilog.Events;
    using Utilities;

    /// <summary>
    ///     The base class for language server event handlers.
    /// </summary>
    public abstract class Handler
        : IJsonRpcHandler
    {
        /// <summary>
        ///     Create a new <see cref="Handler"/>.
        /// </summary>
        /// <param name="server">
        ///     The language server.
        /// </param>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        protected Handler(ILanguageServer server, ILogger logger)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));

            Server = server;
            Log = logger.ForContext(GetType());
        }

        /// <summary>
        ///     The handler's logger.
        /// </summary>
        protected ILogger Log { get; }

        /// <summary>
        ///     The language server.
        /// </summary>
        protected ILanguageServer Server { get; }

        /// <summary>
        ///     Add an activity / log-context scope for an operation.
        /// </summary>
        /// <param name="operationName">
        ///     The operation name.
        /// </param>
        /// <returns>
        ///     An <see cref="IDisposable"/> representing the log-context scope.
        /// </returns>
        protected IDisposable BeginOperation(string operationName)
        {
            if (String.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'operationName'.", nameof(operationName));
            
            return new CompositeDisposable(
                // Activity Id
                ActivityCorrelationManager.BeginActivityScope(),

                // Operation
                Serilog.Context.LogContext.PushProperty("Operation", operationName),

                // Timing
                Log.BeginTimedOperation(operationName,
                    identifier: ActivityCorrelationManager.CurrentActivityId?.ToString(),
                    level: LogEventLevel.Verbose,
                    warnIfExceeds: TimeSpan.FromSeconds(10)
                )
            );
        }
    }
}
